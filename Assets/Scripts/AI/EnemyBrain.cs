using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 근접 적의 판단을 맡는 컴포넌트. PlayerMoveInput이 키를 읽는 자리에서 이쪽은 상황을 읽고,
/// 똑같이 MovementCore.SetMoveInput / AttackCore.TryAttack만 부른다. 그래서 이동·콤보·경직·넉백 규칙이
/// 플레이어와 똑같이 적용된다.
///
/// 상태:
///   Idle     — 플레이어가 감지 거리 안에 들어올 때까지 서 있는다. 한 번 감지하면 다시 돌아오지 않는다.
///   Approach — 플레이어 옆의 '대기 위치'(같은 라인, 사거리보다 standoffMargin만큼 바깥)로 간다.
///              보통 속도로 가고 멀면 달린다. 교전 범위는 보지 않는다.
///              대기 위치에서 engageEnterRadius 안에 들어오면 Engage로.
///   Engage   — stalkSpeedRatio로 천천히 대기 위치를 따라다니며, 플레이어가 교전 범위(적 중심의 원) 안에
///              머문 시간을 센다. engageDuration을 채우면 Rush로. 범위 밖으로 나가면 Approach로 돌아간다.
///              플레이어가 공격 중이면 시간을 멈추고 기다렸다가, 그 공격이 끝나는 순간 곧바로 Rush로(빈틈 공략).
///   Rush     — 공격 위치(사거리 안쪽)로 달려간다. 사거리에 들어오는 순간 바로 친다.
///              돌진 중에 플레이어가 공격을 시작해도 멈추지 않는다 — 받아치기는 플레이어의 몫이다.
///              rushTimeout 안에 못 닿으면 포기하고 Approach로.
///   Attack   — 공격 동작이 끝날 때까지 이동 입력을 넣지 않는다(Lunge 속도가 지워지지 않게).
///              타격이 맞았을 때만 콤보 다음 단계를 잇는다(히트 확인). 빗나가면 그 단계로 끝난다.
///   Recover  — 제자리에서 숨을 고른다. 플레이어가 반격할 틈이다. recoverDuration이 지나면 Approach로.
///
/// 판단 범위(교전 범위)와 공격 사거리를 나눠둔 이유: 사거리 안에서만 시간을 세면 조금만 움직여도
/// 초기화되어 피하기가 너무 쉽다. 넓은 범위에서 재고, 칠 때는 직접 달려들어 거리를 좁힌다.
///
/// 맞는 중(경직·띄워짐·쓰러짐)엔 판단을 멈추고, 풀리면 어느 상태였든 Approach부터 다시 한다.
/// 감지 전에 맞아도 쫓아온다.
///
/// 랜덤 요소는 없다. 같은 상황이면 같은 행동을 한다.
/// 판단 과정을 눈으로 보려면 같은 오브젝트에 EnemyBrainGizmo를 붙인다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 MovementCore + AttackCore + StateMachine, 계층 위쪽에 Character.
///          target에 플레이어의 Character, attackSkill에 이 적이 쓸 공격.
///          같은 적에 EnemyAutoAttackTest / EnemyPatrolTest가 켜져 있으면 입력이 겹치므로 꺼둔다.
/// </summary>
public class EnemyBrain : MonoBehaviour
{
    /// <summary>AI의 판단 상태. EnemyBrainGizmo가 표시하려고 읽으므로 public이다.</summary>
    public enum AIState
    {
        Idle,
        Approach,
        Engage,
        Rush,
        Attack,
        Recover,
    }

    /// <summary>목표 지점에 이만큼 가까우면 도착한 것으로 보고 멈춘다(m). 달리기 한 프레임 이동량보다 커야 지나치지 않는다.</summary>
    public const float ArriveRadius = 0.2f;

    /// <summary>
    /// 돌진할 때 노리는 공격 위치를 사거리 × 이 값만큼 떨어진 곳에 잡는다. 사거리 끝에 딱 맞추면
    /// 그 사이 플레이어가 조금만 물러나도 헛치기 때문에 안쪽으로 당긴다.
    /// </summary>
    const float StandDistanceRatio = 0.7f;

    [Tooltip("노릴 대상. 플레이어의 Character를 연결한다.")]
    [SerializeField] private Character target;

    [Tooltip("이 적이 쓸 공격. 대기 위치·교전 범위·공격 위치를 전부 이 스킬의 사거리에서 계산한다.")]
    [SerializeField] private SkillData attackSkill;

    [Tooltip("플레이어가 이 거리(m) 안에 들어오면 감지하고 다가오기 시작한다.")]
    [SerializeField] private float detectRange = 8f;

    [Tooltip("목표 지점까지 X 거리가 이보다 멀면 달리고, 가까우면 걷는다(m). Engage 중에는 달리지 않는다.")]
    [SerializeField] private float runDistance = 4f;

    [Tooltip("대기 위치가 사거리보다 얼마나 바깥인지(m). 대기 거리 = 사거리 + 이 값.")]
    [SerializeField] private float standoffMargin = 1.5f;

    [Tooltip("Approach 중 대기 위치까지 이 거리(m) 안에 들어오면 Engage로 넘어간다. " +
             "남은 거리는 Engage의 느린 속도로 마저 좁힌다.")]
    [Min(ArriveRadius)]
    [SerializeField] private float engageEnterRadius = 1f;

    [Tooltip("교전 범위가 대기 거리보다 얼마나 넓은지(m). 교전 범위 = 대기 거리 + 이 값. " +
             "0보다 커야 대기 위치에 선 채로 시간이 흐른다.")]
    [Min(0.01f)]
    [SerializeField] private float engageMargin = 1f;

    [Tooltip("Engage 중 플레이어가 교전 범위 안에 이 시간(초) 동안 머물면 돌진한다.")]
    [FormerlySerializedAs("readyDuration")]
    [SerializeField] private float engageDuration = 0.4f;

    [Tooltip("Engage 중 따라다닐 때의 속도 배율. 1이면 보통 걷기, 0.5면 절반 속도.")]
    [Range(0f, 1f)]
    [SerializeField] private float stalkSpeedRatio = 0.5f;

    [Tooltip("돌진해서 이 시간(초) 안에 사거리에 못 들어가면 포기하고 다시 대기로 돌아간다.")]
    [SerializeField] private float rushTimeout = 1f;

    [Tooltip("공격이 끝난 뒤 제자리에서 숨을 고르는 시간(초). 플레이어가 반격할 틈이다.")]
    [SerializeField] private float recoverDuration = 0.8f;

    MovementCore movement;
    AttackCore attack;
    StateMachine state;

    /// <summary>플레이어의 AttackCore. 휘두르는 중인지 보고 빈틈을 노리는 데 쓴다. 없으면 null이고 빈틈 공략을 건너뛴다.</summary>
    AttackCore targetAttack;

    /// <summary>실제로 움직이는 몸. Character의 transform이다.</summary>
    Transform body;

    /// <summary>MapBounds가 없으면 null. 목표 지점이 맵 밖에 잡히지 않게 하는 데 쓴다.</summary>
    MapBounds map;

    AIState aiState = AIState.Idle;

    /// <summary>맞는 중이라 판단을 멈춘 프레임인지. 표시용이다.</summary>
    bool stunned;

    /// <summary>Engage 중 플레이어가 교전 범위 안에 머문 시간.</summary>
    float engageTimer;

    /// <summary>Engage 중 플레이어의 공격을 보고 기다리는 중인지. 그 공격이 끝나는 순간 돌진한다.</summary>
    bool waitingForOpening;

    /// <summary>Rush·Recover에서 남은 시간. 두 상태가 겹칠 일이 없어 하나로 같이 쓴다.</summary>
    float stateTimer;

    /// <summary>마지막으로 향한 목표 지점(대기 위치 또는 공격 위치).</summary>
    Vector3 goal;

    // ── 읽기 전용 정보. EnemyBrainGizmo가 판단 과정을 그리는 데 쓴다.

    /// <summary>지금 판단 상태.</summary>
    public AIState State => aiState;

    /// <summary>맞는 중이라 판단을 멈췄는지. 이때 State는 풀린 뒤 돌아갈 Approach다.</summary>
    public bool IsStunned => stunned;

    /// <summary>Engage 중 교전 범위 안에 머문 시간.</summary>
    public float EngageTimer => engageTimer;

    public float EngageDuration => engageDuration;

    /// <summary>Engage 중 플레이어가 공격하는 걸 보고 빈틈을 기다리는 중인지.</summary>
    public bool IsWaitingForOpening => waitingForOpening;

    /// <summary>Rush·Recover에서 남은 시간.</summary>
    public float StateTimer => stateTimer;

    /// <summary>지금 휘두르고 있는 콤보 단계의 스킬. Awake 전이면 null.</summary>
    public SkillData CurrentAttackSkill => attack != null ? attack.CurrentSkill : null;

    /// <summary>지금 콤보 단계의 타격이 맞았는지.</summary>
    public bool AttackHitLanded => attack != null && attack.HitLanded;

    public float DetectRange => detectRange;

    public float RunDistance => runDistance;

    /// <summary>대기 위치 주변의 Engage 진입 반지름.</summary>
    public float EngageEnterRadius => engageEnterRadius;

    /// <summary>공격 스킬이 연결돼 있는지. 사거리에서 나오는 값들(Reach 등)은 이게 true일 때만 읽을 수 있다.</summary>
    public bool HasSkill => attackSkill != null;

    /// <summary>지금 목표 지점을 향해 움직이는 상태인지(Approach·Engage·Rush). 아니면 Goal은 지난 값이다.</summary>
    public bool HasGoal => aiState == AIState.Approach || aiState == AIState.Engage || aiState == AIState.Rush;

    /// <summary>마지막으로 향한 목표 지점.</summary>
    public Vector3 Goal => goal;

    /// <summary>몸의 위치. Awake 전(에디터에서 Play 전)에는 이 오브젝트의 위치를 돌려준다.</summary>
    public Vector3 BodyPosition => body != null ? body.position : transform.position;

    /// <summary>
    /// 이 스킬로 닿는 X 거리. 판정 상자의 먼 쪽 끝에 Lunge로 밀려나가는 거리를 더한 값이다.
    /// 스킬 수치에서 바로 계산하므로 스킬을 고치면 서는 거리도 따라간다.
    /// </summary>
    public float Reach
    {
        get
        {
            float lunge = attackSkill.attackType == AttackType.LungeAttack ? attackSkill.lungeDistance : 0f;
            return attackSkill.attackDistance + attackSkill.attackBoxSize.x * 0.5f + lunge;
        }
    }

    /// <summary>
    /// 판정 상자의 안쪽 끝까지의 X 거리. 상대가 이보다 가까우면 상자가 몸 앞으로 지나가 헛친다.
    /// 상자가 몸 중심을 덮으면 음수가 되어 사실상 제한이 없다.
    ///
    /// 공격 위치(Reach × StandDistanceRatio)가 이보다 가까우면 도착해도 사거리 밖이라 공격하지 못한다.
    /// 판정 상자가 몸에서 멀리 떨어진 좁은 스킬일 때 그렇게 되니, 그땐 StandDistanceRatio를 키워야 한다.
    /// </summary>
    public float MinReach => attackSkill.attackDistance - attackSkill.attackBoxSize.x * 0.5f;

    /// <summary>이 스킬로 닿는 Z(라인) 차이. 판정 상자 폭의 절반이다.</summary>
    public float ZTolerance => attackSkill.attackBoxSize.z * 0.5f;

    /// <summary>대기 위치까지의 X 거리. 사거리 바로 바깥이다.</summary>
    public float StandoffDistance => Reach + standoffMargin;

    /// <summary>교전 범위(적 중심 원)의 반지름. 대기 거리보다 항상 넓어서, 대기 위치에 서 있으면 시간이 흐른다.</summary>
    public float EngageRange => StandoffDistance + engageMargin;

    void Awake()
    {
        // Core들은 LogicCores 같은 자식으로 내려가 있다. PlayerMoveInput과 같은 방식으로 찾는다.
        movement = GetComponentInChildren<MovementCore>();
        attack = GetComponentInChildren<AttackCore>();
        state = GetComponentInChildren<StateMachine>();

        Character character = GetComponentInParent<Character>();
        if (character != null)
            body = character.transform;

        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (movement == null || attack == null || state == null || body == null || target == null || attackSkill == null)
        {
            Debug.LogError($"{name}: MovementCore / AttackCore / StateMachine / Character / target / 공격 스킬 중 일부를 찾지 못했습니다. AI를 끕니다.", this);
            enabled = false;
            return;
        }

        // 플레이어의 Core도 LogicCores 같은 자식에 있을 수 있어 Character 기준으로 아래를 찾는다.
        targetAttack = target.GetComponentInChildren<AttackCore>();
        if (targetAttack == null)
            Debug.LogWarning($"{name}: target 아래에서 AttackCore를 찾지 못해 빈틈 공략(플레이어 공격 중 대기)을 건너뜁니다.", this);

        map = MapBounds.Instance;
    }

    void Update()
    {
        // 경직·띄워짐·쓰러짐 중엔 아무것도 하지 않는다. SetMoveInput을 부르면 넉백 속도가 지워진다.
        // CanAct를 쓰지 않는 이유: 공격 중에도 false라서, 그걸로 막으면 Attack 상태가 공격이 끝난 걸 보지 못한다.
        // 풀린 뒤엔 무슨 상태였든 Approach부터 — 하던 공격은 TakeHit이 이미 끊었고, 자리도 밀려났다.
        stunned = state.State == CharacterState.Hit || state.State == CharacterState.Airborne || state.IsDown;
        if (stunned)
        {
            aiState = AIState.Approach;
            return;
        }

        switch (aiState)
        {
            case AIState.Idle:
                if (FlatDistance(body.position, target.transform.position) <= detectRange)
                    aiState = AIState.Approach;
                break;

            case AIState.Approach:
                // 처음 자리를 잡을 때까지는 교전 범위를 보지 않는다. 대기 위치 근처(engageEnterRadius)에
                // 들어와야 Engage가 시작된다. 남은 거리는 Engage가 느린 속도로 마저 간다.
                MoveTo(SlotPosition(StandoffDistance), 1f, runDistance);
                if (FlatDistance(body.position, goal) <= engageEnterRadius)
                {
                    aiState = AIState.Engage;
                    engageTimer = 0f;
                    waitingForOpening = false;
                }
                break;

            case AIState.Engage:
                Engage();
                break;

            case AIState.Rush:
                Rush();
                break;

            case AIState.Attack:
                // state.State가 아니라 IsAttacking으로 본다. 상태는 한 프레임 늦게 바뀔 수 있다.
                if (!attack.IsAttacking)
                {
                    aiState = AIState.Recover;
                    stateTimer = recoverDuration;
                }
                else if (attack.HitLanded)
                {
                    // 히트 확인 콤보. 맞혔을 때만 다음 단계를 누른다. 콤보 창이 아직 안 열렸거나
                    // 마지막 단계면 AttackCore가 알아서 버리므로, 열릴 때까지 매 프레임 눌러둔다.
                    // 그래서 2타가 나가는 타이밍은 클립의 OnComboWindowOpen 이벤트(없으면 타격 프레임)가 정한다.
                    // 콤보를 시작한 스킬을 넘겨야 AttackCore가 같은 콤보의 입력으로 알아본다.
                    attack.TryAttack(attackSkill);
                }
                break;

            case AIState.Recover:
                movement.SetMoveInput(Vector2.zero);
                movement.FaceTowards(target.transform.position.x);
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                    aiState = AIState.Approach;
                break;
        }
    }

    /// <summary>
    /// 천천히 대기 위치를 따라다니며 플레이어가 교전 범위 안에 머문 시간을 센다.
    /// engageDuration을 채우면 돌진하고, 범위 밖으로 나가면 Approach로 돌아가 다시 자리를 잡는다.
    ///
    /// 플레이어가 휘두르는 중이면 시간을 멈추고 기다린다. 대기 위치는 사거리 바깥이라 그대로 따라다니면 된다.
    /// 그 공격이 끝나는 순간 남은 시간과 상관없이 곧바로 돌진한다 — 헛손질한 뒤의 빈틈을 노리는 것이다.
    /// 콤보 도중엔 IsAttacking이 계속 켜져 있으므로 콤보가 통째로 끝날 때까지 기다린다.
    /// </summary>
    void Engage()
    {
        if (FlatDistance(body.position, target.transform.position) > EngageRange)
        {
            aiState = AIState.Approach;
            return;
        }

        bool targetAttacking = targetAttack != null && targetAttack.IsAttacking;

        if (targetAttacking)
        {
            waitingForOpening = true;
        }
        else if (waitingForOpening)
        {
            StartRush();
            return;
        }
        else
        {
            engageTimer += Time.deltaTime;
            if (engageTimer >= engageDuration)
            {
                StartRush();
                return;
            }
        }

        // 달리지 않는다 — 천천히 거리를 재는 모습이어야 한다.
        MoveTo(SlotPosition(StandoffDistance), stalkSpeedRatio, float.PositiveInfinity);
    }

    /// <summary>돌진을 시작한다. 이 프레임부터 달려들고, 이미 사거리 안이면 곧바로 친다.</summary>
    void StartRush()
    {
        aiState = AIState.Rush;
        stateTimer = rushTimeout;
        waitingForOpening = false;
        Rush();
    }

    /// <summary>
    /// 공격 위치로 달려가다가, 사거리에 들어오는 순간 친다. rushTimeout 안에 못 닿으면 포기한다.
    /// </summary>
    void Rush()
    {
        if (InRange())
        {
            // 판정·넉백·스프라이트가 전부 FacingDir을 따르므로 치기 직전에 방향을 맞춘다.
            movement.FaceTowards(target.transform.position.x);
            attack.TryAttack(attackSkill);
            aiState = AIState.Attack;
            return;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            aiState = AIState.Approach;
            return;
        }

        // 거리와 상관없이 달린다(runThreshold 0). 뒷걸음질일 때만 MoveTo가 걷기로 바꾼다.
        MoveTo(SlotPosition(Reach * StandDistanceRatio), 1f, 0f);
    }

    /// <summary>
    /// 지금 치면 닿는지. 판정 상자의 안쪽 끝~바깥쪽 끝(Lunge 포함) 사이이고, 라인이 상자 폭 안이어야 한다.
    /// 상대의 몸 두께는 보지 않는다 — 실제로는 조금 더 넓게 맞지만, 그만큼 확실할 때만 친다.
    /// </summary>
    bool InRange()
    {
        Vector3 d = target.transform.position - body.position;
        float dx = Mathf.Abs(d.x);
        return dx >= MinReach && dx <= Reach && Mathf.Abs(d.z) <= ZTolerance;
    }

    /// <summary>
    /// 목표 지점으로 걷거나 달린다. 도착했으면 바로 선다. 어느 쪽이든 늘 플레이어를 바라본다 —
    /// 물러날 때도 등을 보이지 않는다.
    /// </summary>
    /// <param name="speedScale">속도 배율(0~1). 입력 크기를 줄여 느리게 걷는다.</param>
    /// <param name="runThreshold">목표까지 X 거리가 이보다 멀면 달린다. 달리지 않으려면 PositiveInfinity.</param>
    /// <returns>목표 지점에 도착해 있으면 true.</returns>
    bool MoveTo(Vector3 destination, float speedScale, float runThreshold)
    {
        goal = destination;

        Vector3 pos = body.position;
        Vector2 toGoal = new Vector2(goal.x - pos.x, goal.z - pos.z);
        bool arrived = toGoal.sqrMagnitude <= ArriveRadius * ArriveRadius;

        if (arrived)
        {
            // 입력을 아예 안 넣으면 걷던 속도가 마찰로 서서히 죽으며 미끄러지므로, 0을 넣어 바로 세운다.
            movement.SetMoveInput(Vector2.zero);
        }
        else
        {
            // 뒷걸음질(플레이어 반대쪽으로 가는 중)엔 달리지 않는다. 플레이어를 보며 뒤로 달리면 문워크가 된다.
            bool towardTarget = toGoal.x * (target.transform.position.x - pos.x) > 0f;
            bool run = towardTarget && Mathf.Abs(toGoal.x) > runThreshold;
            movement.SetMoveInput(toGoal.normalized * speedScale, run);
        }

        // SetMoveInput이 입력 방향으로 돌려놓은 걸 플레이어 쪽으로 다시 돌린다. 같은 프레임 안이라 깜빡이지 않는다.
        movement.FaceTowards(target.transform.position.x);
        return arrived;
    }

    /// <summary>
    /// 플레이어 옆의 자리. 같은 라인(Z)에서, 지금 내가 있는 쪽으로 distance만큼 떨어진 곳이다.
    /// 그쪽이 맵 밖이면(플레이어가 벽에 붙어 있으면) 반대쪽을 잡는다.
    /// </summary>
    Vector3 SlotPosition(float distance)
    {
        Vector3 t = target.transform.position;
        float side = body.position.x >= t.x ? 1f : -1f;

        if (map != null)
        {
            float x = t.x + side * distance;
            if (x < map.Bounds.min.x || x > map.Bounds.max.x)
                side = -side;
        }

        return new Vector3(t.x + side * distance, body.position.y, t.z);
    }

    /// <summary>높이를 뺀 바닥 평면(X·Z) 거리.</summary>
    static float FlatDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
