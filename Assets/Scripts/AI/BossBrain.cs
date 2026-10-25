using UnityEngine;

/// <summary>
/// 보스의 판단을 맡는 컴포넌트. EnemyBrain과 같은 방식으로 상황을 읽고
/// MovementCore.SetMoveInput / AttackCore.TryAttack만 부른다. 그래서 이동·콤보 규칙이 플레이어와 똑같이 적용된다.
///
/// 상태:
///   Idle      — 플레이어가 감지 거리 안에 들어올 때까지 서 있는다. 한 번 감지하면 다시 돌아오지 않는다.
///   Approach  — 플레이어 옆, 같은 라인(Z)의 일반공격 위치로 간다. 멀면 달린다. 가는 동안 매 프레임 패턴을 고른다:
///                 일반공격 사거리 안이면 → 일반공격.
///                 일반공격 X 사거리 밖이고 돌진 사거리 안이며 쿨타임이 지났으면 → 돌진.
///               다가오다 보면 돌진 사거리의 바깥 끝에 먼저 닿으므로, 돌진은 먼 거리에서 나간다.
///   Telegraph — 멈춰서 고른 패턴의 예고 시간만큼 노려본다. 바라보는 방향은 들어오는 순간 고정한다 —
///               그 사이 뒤로 돌아가거나 라인을 벗어나면 피할 수 있다. 시간이 다 되면 사거리와 상관없이 친다.
///   Attack    — 맞든 빗나가든 콤보를 끝까지 잇는다. 패턴이 늘 똑같아야 플레이어가 외워서 파훼할 수 있다.
///   Recover   — 고른 패턴의 빈틈 시간만큼 제자리에 선다. 플레이어가 반격할 틈이다. 지나면 Approach로.
///   Groggy    — 그로기 게이지가 다 차면 어느 상태에서든 들어온다. 하던 공격을 끊고 제자리에 서서
///               groggyDuration초 동안 슈퍼아머를 끈다. 지나면 슈퍼아머를 다시 켜고 게이지를 비운 뒤 Approach로.
///
/// 슈퍼아머: 시작할 때 StateMachine의 슈퍼아머를 켠다. 맞아도 경직이 없어 판단이 끊기지 않는다.
/// 그로기 동안만 꺼서, 그때는 일반 적처럼 경직·띄우기가 걸려 콤보가 들어간다.
/// 맞는 중(경직·띄워짐·쓰러짐)엔 판단을 멈추지만 그로기 시간은 계속 흐른다.
///
/// 그로기 게이지(임시 조건): 받은 피해만큼 찬다. 그로기 중에 받은 피해는 쌓이지 않는다.
/// 슈퍼아머 중엔 TakeHit이 불리지 않아서 Health.Damaged로 맞은 걸 안다.
///
/// 판단에는 랜덤 요소가 없다. 같은 상황이면 같은 행동을 한다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 MovementCore + AttackCore + StateMachine, 계층 위쪽에 Character,
///          Character 아래 어딘가에 Health. target에 플레이어의 Character, attackSkill에 보스가 쓸 공격.
///          Animator에 Groggy 상태(같은 이름의 반복 클립).
/// </summary>
public class BossBrain : MonoBehaviour
{
    enum AIState
    {
        Idle,
        Approach,
        Telegraph,
        Attack,
        Recover,
        Groggy,
    }

    /// <summary>목표 지점에 이만큼 가까우면 도착한 것으로 보고 멈춘다(m). 달리기 한 프레임 이동량보다 커야 지나치지 않는다.</summary>
    const float ArriveRadius = 0.2f;

    /// <summary>
    /// 공격 위치를 사거리 × 이 값만큼 떨어진 곳에 잡는다. 사거리 끝에 딱 맞추면
    /// 조금만 물러나도 사거리 밖이 되기 때문에 안쪽으로 당긴다.
    /// </summary>
    const float StandDistanceRatio = 0.7f;

    [Tooltip("노릴 대상. 플레이어의 Character를 연결한다.")]
    [SerializeField] private Character target;

    [Tooltip("보스가 쓸 공격. 공격 위치와 사거리를 이 스킬의 수치에서 계산한다. " +
             "다음 공격이 이어져 있으면 콤보 끝까지 쓴다(첫 단계로 순환하게 이어두면 멈추지 않는다).")]
    [SerializeField] private SkillData attackSkill;

    [Tooltip("플레이어가 이 거리(m) 안에 들어오면 감지하고 다가오기 시작한다.")]
    [SerializeField] private float detectRange = 8f;

    [Tooltip("공격 위치까지 X 거리가 이보다 멀면 달리고, 가까우면 걷는다(m).")]
    [SerializeField] private float runDistance = 4f;

    [Tooltip("사거리에 들어온 뒤 멈춰서 노려보는 시간(초). 이 동안 방향이 고정되어, 뒤로 돌아가거나 라인을 벗어나면 피할 수 있다.")]
    [Min(0f)]
    [SerializeField] private float telegraphDuration = 0.6f;

    [Tooltip("공격이 끝난 뒤 제자리에 서 있는 시간(초). 플레이어가 반격할 틈이다.")]
    [Min(0f)]
    [SerializeField] private float recoverDuration = 1.2f;

    [Header("돌진")]
    [Tooltip("일반공격 사거리 밖에서 쓰는 돌진공격. 보통 Lunge Attack에 lungeDistance를 크게 준 스킬이다. " +
             "돌진 사거리는 lungeDistance를 다 간 뒤의 판정 상자 끝까지로 계산하므로, 타격 프레임이 이르면 실제로는 덜 닿는다. " +
             "비워두면 돌진을 쓰지 않는다.")]
    [SerializeField] private SkillData dashSkill;

    [Tooltip("돌진 전에 멈춰서 노려보는 시간(초).")]
    [Min(0f)]
    [SerializeField] private float dashTelegraphDuration = 1f;

    [Tooltip("돌진이 끝난 뒤 제자리에 서 있는 시간(초).")]
    [Min(0f)]
    [SerializeField] private float dashRecoverDuration = 1.8f;

    [Tooltip("돌진을 한 번 쓰면 이 시간(초) 동안 다시 쓰지 않는다. 예고를 시작한 순간부터 센다.")]
    [Min(0f)]
    [SerializeField] private float dashCooldown = 6f;

    [Header("그로기")]
    [Tooltip("그로기 게이지의 최댓값. 받은 피해만큼 차고, 이 값에 닿으면 그로기에 빠진다.")]
    [Min(1f)]
    [SerializeField] private float groggyGaugeMax = 100f;

    [Tooltip("그로기 시간(초). 이 동안 슈퍼아머가 꺼져 콤보가 들어간다. 맞아서 떠 있거나 누워 있는 동안에도 흐른다.")]
    [Min(0f)]
    [SerializeField] private float groggyDuration = 4f;

    MovementCore movement;
    AttackCore attack;
    StateMachine state;
    Health health;

    /// <summary>실제로 움직이는 몸. Character의 transform이다.</summary>
    Transform body;

    /// <summary>MapBounds가 없으면 null. 공격 위치가 맵 밖에 잡히지 않게 하는 데 쓴다.</summary>
    MapBounds map;

    AIState aiState = AIState.Idle;

    /// <summary>Telegraph·Recover·Groggy에서 남은 시간. 세 상태가 겹칠 일이 없어 하나로 같이 쓴다.</summary>
    float stateTimer;

    /// <summary>이번에 고른 패턴의 스킬. Telegraph에서 고르고 Attack이 쓴다.</summary>
    SkillData patternSkill;

    /// <summary>이번에 고른 패턴의 빈틈 시간. Recover가 쓴다.</summary>
    float patternRecoverDuration;

    /// <summary>돌진을 다시 쓸 수 있을 때까지 남은 시간. 0 이하면 쓸 수 있다.</summary>
    float dashCooldownTimer;

    /// <summary>지금 그로기 게이지. 그로기 동안은 꽉 찬 채로 있다가 끝날 때 0이 된다.</summary>
    float groggyGauge;

    /// <summary>지금 그로기 게이지. BossStatusBars가 바를 그리는 데 쓴다.</summary>
    public float GroggyGauge => groggyGauge;

    public float GroggyGaugeMax => groggyGaugeMax;

    /// <summary>
    /// 이 스킬로 닿는 X 거리. 판정 상자의 먼 쪽 끝에 Lunge로 밀려나가는 거리를 더한 값이다.
    /// EnemyBrain.Reach와 같은 계산이다.
    /// </summary>
    static float Reach(SkillData skill)
    {
        float lunge = skill.attackType == AttackType.LungeAttack ? skill.lungeDistance : 0f;
        return skill.attackDistance + skill.attackBoxSize.x * 0.5f + lunge;
    }

    /// <summary>판정 상자의 안쪽 끝까지의 X 거리. 상대가 이보다 가까우면 상자가 몸 앞으로 지나가 헛친다.</summary>
    static float MinReach(SkillData skill) => skill.attackDistance - skill.attackBoxSize.x * 0.5f;

    /// <summary>이 스킬로 닿는 Z(라인) 차이. 판정 상자 폭의 절반이다.</summary>
    static float ZTolerance(SkillData skill) => skill.attackBoxSize.z * 0.5f;

    void Awake()
    {
        // Core들은 LogicCores 같은 자식으로 내려가 있다. EnemyBrain과 같은 방식으로 찾는다.
        movement = GetComponentInChildren<MovementCore>();
        attack = GetComponentInChildren<AttackCore>();
        state = GetComponentInChildren<StateMachine>();

        // Health는 Core들과 다른 가지에 있을 수 있어 Character 기준으로 아래를 찾는다.
        Character character = GetComponentInParent<Character>();
        if (character != null)
        {
            body = character.transform;
            health = character.GetComponentInChildren<Health>();
        }

        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (movement == null || attack == null || state == null || body == null || health == null || target == null || attackSkill == null)
        {
            Debug.LogError($"{name}: MovementCore / AttackCore / StateMachine / Character / Health / target / 공격 스킬 중 일부를 찾지 못했습니다. AI를 끕니다.", this);
            enabled = false;
            return;
        }

        state.HasSuperArmor = true;
        map = MapBounds.Instance;
    }

    // Awake에서 스스로 꺼졌으면(준비물 누락) OnEnable이 불리지 않는다.
    void OnEnable()
    {
        health.Damaged += OnDamaged;
    }

    // Awake에서 꺼진 경우를 대비해 null을 본다.
    void OnDisable()
    {
        if (health != null)
            health.Damaged -= OnDamaged;
    }

    void Update()
    {
        // 죽었으면 판단을 멈춘다.
        if (state.IsDead)
        {
            enabled = false;
            return;
        }

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        // 그로기 시간은 맞는 중(경직·띄워짐·쓰러짐)에도 흐른다 — 그 사이가 곧 콤보를 넣는 시간이다.
        // 떠 있는 채로 끝나면 착지·누움·일어서기는 그대로 하고, 그 뒤로 들어오는 공격은 다시 슈퍼아머가 막는다.
        if (aiState == AIState.Groggy)
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
                EndGroggy();
        }

        // 슈퍼아머가 꺼진 그로기 중에만 여기 걸린다. 맞는 중에 SetMoveInput을 부르면 넉백 속도가 지워진다.
        // 그로기가 끝났으면 풀린 뒤 다시 다가간다 — 자리가 밀려났다.
        if (state.State == CharacterState.Hit || state.State == CharacterState.Airborne || state.IsDown)
        {
            if (aiState != AIState.Groggy)
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
                if (InRange(attackSkill))
                {
                    StartTelegraph(attackSkill, telegraphDuration, recoverDuration);
                    break;
                }

                if (DashUsable())
                {
                    StartTelegraph(dashSkill, dashTelegraphDuration, dashRecoverDuration);
                    dashCooldownTimer = dashCooldown;
                    break;
                }

                MoveTo(AttackPosition());
                break;

            case AIState.Telegraph:
                // 0을 넣어 바로 세운다. 좌우 입력이 0이라 SetMoveInput이 방향을 바꾸지 않는다.
                movement.SetMoveInput(Vector2.zero);
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    attack.TryAttack(patternSkill);
                    aiState = AIState.Attack;
                }
                break;

            case AIState.Attack:
                // state.State가 아니라 IsAttacking으로 본다. 상태는 한 프레임 늦게 바뀔 수 있다.
                if (!attack.IsAttacking)
                {
                    aiState = AIState.Recover;
                    stateTimer = patternRecoverDuration;
                }
                else
                {
                    // 맞았는지와 상관없이 다음 단계를 누른다. 콤보 창이 아직 안 열렸거나 마지막 단계면
                    // AttackCore가 알아서 버리므로, 열릴 때까지 매 프레임 눌러둔다.
                    // 이동 입력은 넣지 않는다 — Lunge 속도가 지워진다.
                    attack.TryAttack(patternSkill);
                }
                break;

            case AIState.Recover:
                StandStill();
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                    aiState = AIState.Approach;
                break;

            case AIState.Groggy:
                // 제자리에 선다. 플레이어 쪽으로 돌아보지도 않는다. 시간은 위에서 센다.
                movement.SetMoveInput(Vector2.zero);
                break;
        }
    }

    /// <summary>받은 피해만큼 그로기 게이지를 채운다. 다 차면 그로기에 빠진다. Health.Damaged가 부른다.</summary>
    void OnDamaged(float amount)
    {
        if (state.IsDead || aiState == AIState.Groggy) return;

        groggyGauge = Mathf.Min(groggyGauge + amount, groggyGaugeMax);
        if (groggyGauge >= groggyGaugeMax)
            StartGroggy();
    }

    /// <summary>
    /// 그로기에 빠진다. 하던 공격을 끊고 슈퍼아머를 끈다. 게이지는 꽉 찬 채로 둔다.
    /// 플레이어의 타격 판정 도중에 불리지만, 그 타격의 넉백·경직은 이미 건너뛴 뒤라 이 타격으로는 굳지 않는다.
    /// </summary>
    void StartGroggy()
    {
        attack.Cancel();
        state.HasSuperArmor = false;
        state.IsGroggy = true;
        aiState = AIState.Groggy;
        stateTimer = groggyDuration;
        Debug.Log($"[그로기] {name} 그로기 시작", this);
    }

    /// <summary>그로기에서 깨어난다. 슈퍼아머를 다시 켜고 게이지를 비운 뒤 다시 다가간다.</summary>
    void EndGroggy()
    {
        state.HasSuperArmor = true;
        state.IsGroggy = false;
        groggyGauge = 0f;
        aiState = AIState.Approach;
        Debug.Log($"[그로기] {name} 그로기 끝", this);
    }

    /// <summary>
    /// 패턴을 골라 예고를 시작한다. 방향은 여기서 한 번만 맞추고 공격이 끝날 때까지 고정한다.
    /// 판정·Lunge가 FacingDir을 따르므로, 이 사이 뒤로 돌아간 플레이어는 맞지 않는다.
    /// </summary>
    void StartTelegraph(SkillData skill, float telegraph, float recover)
    {
        movement.FaceTowards(target.transform.position.x);
        movement.SetMoveInput(Vector2.zero);
        patternSkill = skill;
        patternRecoverDuration = recover;
        aiState = AIState.Telegraph;
        stateTimer = telegraph;
    }

    /// <summary>
    /// 지금 돌진을 쓸 수 있는지. 돌진 스킬이 있고, 쿨타임이 지났고, 플레이어가 일반공격의 X 사거리 밖이면서
    /// 돌진 사거리 안이어야 한다. 일반공격은 라인(Z)이 안 맞아서 못 치는 것일 수도 있으므로 X 거리로만 가른다 —
    /// 그래야 가까이 붙어 있을 때 돌진이 나가지 않는다.
    /// </summary>
    bool DashUsable()
    {
        if (dashSkill == null || dashCooldownTimer > 0f) return false;

        float dx = Mathf.Abs(target.transform.position.x - body.position.x);
        return dx > Reach(attackSkill) && InRange(dashSkill);
    }

    /// <summary>
    /// 이 스킬로 지금 치면 닿는지. 판정 상자의 안쪽 끝~바깥쪽 끝(Lunge 포함) 사이이고, 라인이 상자 폭 안이어야 한다.
    /// EnemyBrain.InRange와 같은 계산이다.
    /// </summary>
    bool InRange(SkillData skill)
    {
        Vector3 d = target.transform.position - body.position;
        float dx = Mathf.Abs(d.x);
        return dx >= MinReach(skill) && dx <= Reach(skill) && Mathf.Abs(d.z) <= ZTolerance(skill);
    }

    /// <summary>
    /// 플레이어 옆의 일반공격 위치. 같은 라인(Z)에서, 지금 보스가 있는 쪽(좌/우)으로 사거리 안쪽만큼 떨어진 곳이다.
    /// 그쪽이 맵 밖이면(플레이어가 벽에 붙었으면) 반대쪽으로 잡는다. 안 그러면 닿을 수 없는 자리로 영영 걷는다.
    /// </summary>
    Vector3 AttackPosition()
    {
        Vector3 t = target.transform.position;
        float distance = Reach(attackSkill) * StandDistanceRatio;
        int side = body.position.x >= t.x ? 1 : -1;

        float x = t.x + side * distance;
        if (map != null && (x < map.Bounds.min.x || x > map.Bounds.max.x))
            x = t.x - side * distance;

        return new Vector3(x, body.position.y, t.z);
    }

    /// <summary>
    /// 목표 지점으로 걷거나 달린다. 도착했으면 선다. 어느 쪽이든 늘 플레이어를 바라본다.
    /// 뒷걸음질(플레이어 반대쪽으로 가는 중)엔 달리지 않는다. 플레이어를 보며 뒤로 달리면 문워크가 된다.
    /// </summary>
    void MoveTo(Vector3 destination)
    {
        Vector3 pos = body.position;
        Vector2 toGoal = new Vector2(destination.x - pos.x, destination.z - pos.z);

        if (toGoal.sqrMagnitude <= ArriveRadius * ArriveRadius)
        {
            StandStill();
            return;
        }

        bool towardTarget = toGoal.x * (target.transform.position.x - pos.x) > 0f;
        bool run = towardTarget && Mathf.Abs(toGoal.x) > runDistance;
        movement.SetMoveInput(toGoal.normalized, run);

        // SetMoveInput이 입력 방향으로 돌려놓은 걸 플레이어 쪽으로 다시 돌린다. 같은 프레임 안이라 깜빡이지 않는다.
        movement.FaceTowards(target.transform.position.x);
    }

    /// <summary>서서 플레이어를 바라본다. 입력을 아예 안 넣으면 마찰로 미끄러지므로 0을 넣어 바로 세운다.</summary>
    void StandStill()
    {
        movement.SetMoveInput(Vector2.zero);
        movement.FaceTowards(target.transform.position.x);
    }

    /// <summary>높이를 뺀 바닥 평면(X·Z) 거리.</summary>
    static float FlatDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
