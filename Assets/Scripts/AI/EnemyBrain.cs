using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 근접 적의 판단을 맡는 컴포넌트. PlayerMoveInput이 키를 읽는 자리에서 이쪽은 상황을 읽고,
/// 똑같이 MovementCore.SetMoveInput / AttackCore.TryAttack만 부른다. 그래서 이동·콤보·경직·넉백 규칙이
/// 플레이어와 똑같이 적용되고, AI만 규칙을 어기는 일이 없다.
///
/// 상태:
///   Hover    — 사거리 바깥에서 거리를 재며 서성인다. 공격권을 얻으면 Approach로.
///   Approach — 공격권을 쥐고 다가간다. 라인(Z)을 먼저 맞추고, 사거리 안이면 Attack으로.
///   Attack   — 공격 동작이 끝날 때까지 입력을 넣지 않는다(Lunge 속도가 지워지지 않게).
///   Recover  — 뒤로 빠지며 숨을 돌린다. 끝나면 Hover로.
///
/// 판단은 매 프레임이 아니라 thinkInterval 간격으로 한다(반응 지연). 판단 사이에는 직전 결정대로 움직인다.
/// 플레이어 위치도 판단하는 순간에만 본다. 이동·방향은 그 '본 위치' 기준이라, 적은 플레이어가 조금 전에
/// 있던 곳으로 가고 다음 판단 때 고친다. 행동이 바뀔 때마다(다가가기·걷기 시작·물러나기 등)
/// reactionDelay만큼 제자리에서 멈칫한 뒤 움직인다. 공격만은 예외라, 다가가다 사거리에 닿는 순간 바로 친다.
///
/// 여러 마리의 협동: 공격권은 씬 전체에 하나뿐이라 동시에 공격에 들어가는 적은 한 마리다.
/// 캐릭터끼리 몸 충돌이 없어서, 서로 가까우면 이동 입력에 밀어내는 방향을 더해 겹치지 않게 한다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 MovementCore + AttackCore + StateMachine, 계층 위쪽에 Character.
///          target에 플레이어의 Character, attackSkill에 쓸 공격.
///          같은 적에 EnemyAutoAttackTest가 켜져 있으면 둘이 동시에 공격을 넣으므로 꺼둔다.
/// </summary>
public class EnemyBrain : MonoBehaviour
{
    enum AIState
    {
        Hover,
        Approach,
        Attack,
        Recover,
    }

    /// <summary>목표 지점에 이만큼 가까우면 도착한 것으로 보고 멈춘다(m).</summary>
    const float ArriveRadius = 0.2f;

    /// <summary>상대와 X 거리가 이보다 가까우면 방향을 바꾸지 않는다. 바로 위아래에 섰을 때 좌우로 떨리는 걸 막는다.</summary>
    const float FaceDeadZone = 0.1f;

    /// <summary>Approach에서 서려는 거리 = 사거리 × 이 값. 딱 경계에 서면 Lunge가 덜 나갔을 때 헛친다.</summary>
    const float ApproachDistanceRatio = 0.7f;

    /// <summary>Scene 뷰에서 상태 기즈모를 그릴 높이(몸 위치 기준, m).</summary>
    const float GizmoHeight = 2f;

    /// <summary>켜져 있는 모든 적. 서로 밀어내는 데 쓴다.</summary>
    static readonly List<EnemyBrain> active = new List<EnemyBrain>();

    /// <summary>지금 공격권을 쥔 적. 없으면 null.</summary>
    static EnemyBrain tokenHolder;

    [Tooltip("노릴 대상. 플레이어의 Character를 연결한다.")]
    [SerializeField] private Character target;

    [Tooltip("쓸 공격. 이 스킬의 상태 이름이 이 적의 애니메이터에 있어야 한다. 사거리도 이 스킬의 수치로 계산한다.")]
    [SerializeField] private SkillData attackSkill;

    [Header("판단")]
    [Tooltip("판단 간격의 최소값(초). 이 사이의 랜덤 간격으로 판단한다. 짧을수록 반응이 빠르다.")]
    [SerializeField] private float thinkIntervalMin = 0.3f;

    [Tooltip("판단 간격의 최대값(초).")]
    [SerializeField] private float thinkIntervalMax = 0.6f;

    [Tooltip("행동이 바뀔 때 제자리에서 멈칫하는 시간의 최소값(초). 다가가기·걷기 시작·물러나기마다 이 사이의 랜덤 시간만큼 선다. 공격 직전에는 멈칫하지 않는다.")]
    [SerializeField] private float reactionDelayMin = 0.15f;

    [Tooltip("행동이 바뀔 때 제자리에서 멈칫하는 시간의 최대값(초).")]
    [SerializeField] private float reactionDelayMax = 0.35f;

    [Tooltip("공격이 끝나거나 맞은 뒤 다시 공격권을 노리기까지 기다리는 시간(초).")]
    [SerializeField] private float attackCooldown = 1.5f;

    [Tooltip("공격권이 비어 있을 때, 판단 한 번마다 공격하러 들어갈 확률.")]
    [Range(0f, 1f)]
    [SerializeField] private float attackChance = 0.3f;

    [Tooltip("플레이어가 나를 못 맞히는 공격을 휘두르는 중일 때의 공격 확률. 헛손질 뒤 빈틈을 노린다.")]
    [Range(0f, 1f)]
    [SerializeField] private float whiffPunishChance = 0.8f;

    [Tooltip("다가가다 이 시간(초) 안에 사거리에 못 들어가면 포기하고 공격권을 넘긴다. 도망치는 상대를 한 마리가 계속 쫓지 않게.")]
    [SerializeField] private float approachTimeout = 3f;

    [Header("거리")]
    [Tooltip("공격권이 없을 때 사거리보다 이만큼 더 떨어져 선다(m). 실제로는 적마다 0~1m씩 더 떨어져 선다.")]
    [SerializeField] private float waitDistanceExtra = 1.5f;

    [Tooltip("멈춰 있다가 다시 걷기 시작하는 거리(m). 목표 지점이 이보다 멀어져야 움직인다. " +
             "플레이어의 작은 움직임에 일일이 따라붙지 않게 한다. 공격하러 다가가는 중에는 쓰지 않는다.")]
    [SerializeField] private float repositionThreshold = 0.8f;

    [Tooltip("공격 후 뒤로 빠지는 시간의 최소값(초).")]
    [SerializeField] private float recoverTimeMin = 0.4f;

    [Tooltip("공격 후 뒤로 빠지는 시간의 최대값(초).")]
    [SerializeField] private float recoverTimeMax = 0.8f;

    [Tooltip("다른 적과 이 거리(m) 안이면 서로 밀어낸다.")]
    [SerializeField] private float separationRadius = 1.2f;

    Character character;
    Transform body;
    MovementCore movement;
    AttackCore attack;
    StateMachine state;

    MovementCore targetMove;
    AttackCore targetAttack;

    AIState aiState = AIState.Hover;

    float thinkTimer;
    float cooldownTimer;
    float recoverTimer;
    float approachTimer;

    /// <summary>남은 멈칫 시간. 0보다 크면 제자리에 서서 아무 판단도 하지 않는다.</summary>
    float pauseTimer;

    /// <summary>목표 지점으로 걷는 중인지. 도착하면 꺼지고, 목표가 크게 벗어나면 멈칫한 뒤 다시 켜진다.</summary>
    bool walking;

    /// <summary>
    /// 마지막으로 판단할 때 본 플레이어 위치. 이동과 방향은 실제 위치가 아니라 이걸 따른다 —
    /// 실제 위치를 매 프레임 따라가면 플레이어 움직임에 싱크된 것처럼 보인다.
    /// </summary>
    Vector3 seenTargetPos;

    /// <summary>이 적만의 대기 거리 가산. 여러 마리가 같은 거리에 줄서지 않게 Awake에서 한 번 정한다.</summary>
    float personalOffset;

    /// <summary>대기 거리의 흔들림. Hover 중 가끔 바뀌어서 들어왔다 빠졌다 하며 거리를 재는 것처럼 보인다.</summary>
    float hoverWobble;

    /// <summary>
    /// 이 스킬로 닿는 X 거리. 판정 상자의 먼 쪽 끝에 Lunge로 밀려나가는 거리를 더한 값이다.
    /// 스킬 수치에서 바로 계산하므로 스킬을 고치면 AI가 서는 거리도 따라간다.
    /// </summary>
    float Reach
    {
        get
        {
            float lunge = attackSkill.attackType == AttackType.LungeAttack ? attackSkill.lungeDistance : 0f;
            return attackSkill.attackDistance + attackSkill.attackBoxSize.x * 0.5f + lunge;
        }
    }

    /// <summary>이 스킬로 닿는 Z(라인) 차이. 판정 상자 폭의 절반이다.</summary>
    float ZTolerance => attackSkill.attackBoxSize.z * 0.5f;

    void Awake()
    {
        // Core들은 LogicCores 같은 자식으로 내려가 있다. PlayerMoveInput과 같은 방식으로 찾는다.
        movement = GetComponentInChildren<MovementCore>();
        attack = GetComponentInChildren<AttackCore>();
        state = GetComponentInChildren<StateMachine>();
        character = GetComponentInParent<Character>();

        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (target != null)
        {
            targetMove = target.GetComponentInChildren<MovementCore>();
            targetAttack = target.GetComponentInChildren<AttackCore>();
        }

        if (movement == null || attack == null || state == null || character == null || attackSkill == null
            || target == null || targetMove == null || targetAttack == null)
        {
            Debug.LogError($"{name}: Core / Character / 공격 스킬 / 대상(Character·MovementCore·AttackCore) 중 일부를 찾지 못했습니다. AI를 끕니다.", this);
            enabled = false;
            return;
        }

        if (target == character)
        {
            Debug.LogError($"{name}: 대상이 자기 자신입니다. AI를 끕니다.", this);
            enabled = false;
            return;
        }

        body = character.transform;
        seenTargetPos = target.transform.position;
        personalOffset = Random.Range(0f, 1f);
        thinkTimer = Random.Range(thinkIntervalMin, thinkIntervalMax);

        // 시작하자마자 달려들지 않게 첫 공격도 쿨다운만큼 기다린다.
        cooldownTimer = attackCooldown;
    }

    void OnEnable()
    {
        active.Add(this);
    }

    // 꺼지거나 플레이 모드가 끝날 때 공격권을 쥔 채로 사라지면 다른 적이 영영 공격하지 못한다.
    void OnDisable()
    {
        active.Remove(this);
        ReleaseToken();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (cooldownTimer > 0f)
            cooldownTimer -= dt;

        // 맞는 중(띄워짐·쓰러짐 포함)엔 아무것도 하지 않는다. SetMoveInput을 부르면 넉백 속도가 지워진다.
        // 하던 공격은 StateMachine.TakeHit이 이미 끊었으니, 공격권도 넘기고 처음부터 다시 간을 본다.
        // 경직이 풀리면 선 상태에서 다시 시작하므로, 걷기 전에 Move가 한 박자를 둔다.
        if (state.State == CharacterState.Hit || state.State == CharacterState.Airborne || state.IsDown)
        {
            ReleaseToken();
            aiState = AIState.Hover;
            pauseTimer = 0f;
            walking = false;
            return;
        }

        // 공격 동작 중엔 입력을 넣지 않는다. SetMoveInput을 부르면 Lunge 속도가 지워진다.
        // state.State가 아니라 IsAttacking을 보는 이유: 상태는 한 프레임 늦게 바뀔 수 있다.
        if (aiState == AIState.Attack)
        {
            if (attack.IsAttacking) return;
            StartRecover();
        }

        // 멈칫하는 동안엔 제자리에 서서 판단도 하지 않는다. 물러남·접근 시간도 흐르지 않는다.
        if (pauseTimer > 0f)
        {
            pauseTimer -= dt;
            StandStill();
            return;
        }

        // 다가가는 중엔 사거리를 판단 간격이 아니라 매 프레임 본다. 닿는 순간 바로 친다 —
        // 판단 때만 보면 도착한 뒤 다음 판단까지 멍하니 서 있어서 멈칫하는 것처럼 보인다.
        if (aiState == AIState.Approach && InRange())
        {
            StartAttack();
            return;
        }

        if (aiState == AIState.Recover)
        {
            recoverTimer -= dt;
            if (recoverTimer <= 0f)
            {
                aiState = AIState.Hover;
                Hesitate();
                return;
            }
        }
        else if (aiState == AIState.Approach)
        {
            approachTimer -= dt;
            if (approachTimer <= 0f)
            {
                ReleaseToken();
                cooldownTimer = attackCooldown;
                aiState = AIState.Hover;
            }
        }

        Move();

        thinkTimer -= dt;
        if (thinkTimer <= 0f)
        {
            thinkTimer = Random.Range(thinkIntervalMin, thinkIntervalMax);
            Think();
        }
    }

    /// <summary>
    /// 반응 지연을 거친 판단. Hover에서 공격하러 들어갈지를 정한다.
    /// 들어간 뒤 언제 칠지는 여기가 아니라 Update가 매 프레임 사거리를 보고 정한다.
    /// </summary>
    void Think()
    {
        // 플레이어 위치는 판단할 때만 본다. 다음 판단까지는 이 위치를 기준으로 움직인다.
        seenTargetPos = target.transform.position;

        switch (aiState)
        {
            case AIState.Hover:
                if (TryTakeToken())
                {
                    aiState = AIState.Approach;
                    approachTimer = approachTimeout;
                    Hesitate();
                }
                else if (Random.value < 0.3f)
                {
                    // 들어왔다 빠졌다. 음수면 대기 거리보다 조금 들어오고, 양수면 물러난다.
                    hoverWobble = Random.Range(-0.5f, 0.8f);
                }
                break;
        }
    }

    /// <summary>
    /// 다음 행동 전에 제자리에서 잠깐 멈칫한다. 멈칫이 끝나면 곧바로 목표 지점으로 걷는다 —
    /// 행동을 바꾸려고 멈춘 것이니, 끝나고 또 멈칫하면 두 번 쉬게 된다.
    /// </summary>
    void Hesitate()
    {
        pauseTimer = Random.Range(reactionDelayMin, reactionDelayMax);
        walking = true;
    }

    /// <summary>
    /// 걷지 않고 선다. 입력을 아예 안 넣으면 걷던 속도가 마찰로 서서히 죽으며 미끄러지므로, 0을 넣어 바로 세운다.
    /// 방향만은 본 위치 쪽으로 돌린다.
    /// </summary>
    void StandStill()
    {
        movement.SetMoveInput(Vector2.zero);
        FaceSeenTarget();
    }

    void FaceSeenTarget()
    {
        float faceX = seenTargetPos.x - body.position.x;
        if (Mathf.Abs(faceX) > FaceDeadZone)
            movement.Face(faceX);
    }

    /// <summary>
    /// 공격권을 노린다. 쿨다운 중이거나 다른 적이 쥐고 있으면 실패한다.
    /// 플레이어가 나를 못 맞히는 공격을 휘두르는 중이면 더 높은 확률로 들어간다.
    /// </summary>
    bool TryTakeToken()
    {
        if (cooldownTimer > 0f) return false;
        if (tokenHolder != null && tokenHolder != this) return false;

        float chance = IsTargetWhiffing() ? whiffPunishChance : attackChance;
        if (Random.value >= chance) return false;

        tokenHolder = this;
        return true;
    }

    void ReleaseToken()
    {
        if (tokenHolder == this)
            tokenHolder = null;
    }

    /// <summary>
    /// 플레이어가 공격 중이고, 그 판정 상자가 지금 내 위치를 덮지 않는지. 즉 나에게는 헛손질인지.
    /// AttackCore.ExecuteHit과 같은 식으로 상자를 세운다. 상자와 내 몸 크기의 겹침까지는 보지 않는 어림셈이다.
    /// </summary>
    bool IsTargetWhiffing()
    {
        if (!targetAttack.IsAttacking) return false;

        SkillData skill = targetAttack.CurrentSkill;
        if (skill == null) return false;

        Vector3 center = target.transform.position + targetMove.FacingDir * skill.attackDistance;
        Vector3 half = skill.attackBoxSize * 0.5f;
        Vector3 d = body.position - center;

        return Mathf.Abs(d.x) > half.x || Mathf.Abs(d.z) > half.z;
    }

    /// <summary>지금 공격하면 닿는지. 공중에서 시작하면 점프 공격 취급이 되므로 지상일 때만 본다.</summary>
    bool InRange()
    {
        if (!movement.IsGrounded) return false;

        Vector3 d = target.transform.position - body.position;
        return Mathf.Abs(d.x) <= Reach && Mathf.Abs(d.z) <= ZTolerance;
    }

    void StartAttack()
    {
        // Lunge는 공격이 시작되는 순간의 방향으로 나가므로, 방향을 먼저 돌리고 같은 프레임에 공격한다.
        movement.Face(target.transform.position.x - body.position.x);
        attack.TryAttack(attackSkill);

        // 클립을 못 찾으면 공격이 통째로 불발된다(AttackCore.Begin 참고). 그대로 물러난다.
        if (attack.IsAttacking)
            aiState = AIState.Attack;
        else
            StartRecover();
    }

    void StartRecover()
    {
        ReleaseToken();
        cooldownTimer = attackCooldown;
        recoverTimer = Random.Range(recoverTimeMin, recoverTimeMax);
        aiState = AIState.Recover;

        // 공격을 마친 자리에 선 것으로 친다. 뒤로 빠지기 전에 Move가 한 박자를 둔다.
        walking = false;
    }

    /// <summary>
    /// 상대 옆의 목표 지점으로 걷는다. 지금 서 있는 쪽(좌/우)을 유지하고, 거리는 상태가 정한다.
    /// 목표 지점은 실제 위치가 아니라 마지막 판단 때 본 위치(seenTargetPos) 기준이다.
    /// 라인(Z)이 어긋나 있으면 X 이동을 줄여 라인부터 맞춘다 — 옆으로 와서 허공을 치는 게 가장 어색하다.
    /// 걷는 방향과 상관없이 늘 상대를 바라본다. 물러날 때도 등을 보이지 않는다.
    ///
    /// 도착하면 서고, 서 있다가는 목표가 repositionThreshold 넘게 벗어나야 멈칫한 뒤 다시 걷는다.
    /// Approach는 이미 공격하러 들어가는 중이라 작은 어긋남도 바로잡는다(ArriveRadius만 넘으면 걷는다).
    /// </summary>
    void Move()
    {
        Vector3 pos = body.position;

        float side = pos.x >= seenTargetPos.x ? 1f : -1f;
        Vector2 toGoal = new Vector2(seenTargetPos.x + side * DesiredDistance() - pos.x, seenTargetPos.z - pos.z);
        float distSq = toGoal.sqrMagnitude;

        if (walking)
        {
            if (distSq <= ArriveRadius * ArriveRadius)
                walking = false;
        }
        else
        {
            float threshold = aiState == AIState.Approach ? ArriveRadius : repositionThreshold;
            if (distSq > threshold * threshold)
            {
                // 걷기 시작하기 전에 한 박자. 이번 프레임은 선 채로 둔다.
                Hesitate();
                StandStill();
                return;
            }
        }

        Vector2 input = Vector2.zero;
        if (walking)
        {
            if (Mathf.Abs(toGoal.y) > ZTolerance)
                toGoal.x *= 0.5f;

            input = toGoal.normalized;
        }

        // 서 있어도 다른 적과 겹치면 밀려난다.
        input += Separation();
        movement.SetMoveInput(Vector2.ClampMagnitude(input, 1f));

        // SetMoveInput이 걷는 방향으로 돌려놓은 것을 상대 쪽으로 다시 돌린다.
        FaceSeenTarget();
    }

    /// <summary>상태별로 상대와 벌리려는 X 거리.</summary>
    float DesiredDistance()
    {
        if (aiState == AIState.Approach)
            return Reach * ApproachDistanceRatio;

        float wait = Reach + waitDistanceExtra + personalOffset;
        return aiState == AIState.Hover ? wait + hoverWobble : wait;
    }

    /// <summary>
    /// 가까운 다른 적에게서 멀어지는 방향. 가까울수록 세다. 캐릭터끼리 몸 충돌이 없어서 이게 없으면 한 점에 겹친다.
    /// 적은 많아야 몇 마리라 매 프레임 전부 훑어도 된다. List의 foreach는 할당이 없다.
    /// </summary>
    Vector2 Separation()
    {
        Vector2 push = Vector2.zero;
        Vector3 pos = body.position;

        foreach (EnemyBrain other in active)
        {
            if (other == this) continue;

            Vector3 d3 = pos - other.body.position;
            Vector2 d = new Vector2(d3.x, d3.z);
            float dist = d.magnitude;

            // 정확히 같은 자리면 밀 방향이 없다. 한 프레임만 지나도 다른 이동으로 어긋난다.
            if (dist >= separationRadius || dist < 0.0001f) continue;

            push += d / dist * (1f - dist / separationRadius);
        }

        return push;
    }

    // 플레이 중 Scene 뷰에서 머리 위 구의 색으로 상태를 보여준다.
    // 대기 = 청록, 접근 = 노랑, 공격 = 빨강, 물러남 = 회색.
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || body == null) return;

        switch (aiState)
        {
            case AIState.Hover: Gizmos.color = Color.cyan; break;
            case AIState.Approach: Gizmos.color = Color.yellow; break;
            case AIState.Attack: Gizmos.color = Color.red; break;
            default: Gizmos.color = Color.gray; break;
        }

        Gizmos.DrawSphere(body.position + Vector3.up * GizmoHeight, 0.2f);
    }
}
