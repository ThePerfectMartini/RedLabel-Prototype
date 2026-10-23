using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 근접 적의 판단을 맡는 컴포넌트. PlayerMoveInput이 키를 읽는 자리에서 이쪽은 상황을 읽고,
/// 똑같이 MovementCore.SetMoveInput / AttackCore.TryAttack만 부른다. 그래서 이동·콤보·경직·넉백 규칙이
/// 플레이어와 똑같이 적용된다.
///
/// 상태:
///   Idle     — 플레이어가 감지 거리 안에 들어올 때까지 서 있는다. 한 번 감지하면 다시 돌아오지 않는다.
///   Hold     — 공격권이 없는 적. 바깥 링(대기 거리 + outerMargin, 라인을 zSpread만큼 위나 아래로 비낌)에서
///              따라다니며 순서를 기다린다. 같은 쪽 Hold 적끼리는 플레이어에 가까운 순으로 자리를 나눠 선다
///              (두 마리씩 위아래, 그 뒤로는 holdSpacing만큼 바깥 줄). 내가 선 쪽(플레이어 기준 좌/우)의 공격권이 비면,
///              그쪽에서 Hold 중인 적 가운데 가장 가까운 적이 가져간다.
///              자리는 느슨하게 쫓는다 — 적마다 다른 불감대·반응 시간이 있고, 서 있는 동안 가끔 반 걸음 옮긴다.
///   Approach — 공격권 쪽(좌/우)의 '대기 위치'(같은 라인, 사거리보다 standoffMargin만큼 바깥)로 간다.
///              보통 속도로 가고 멀면 달린다. 교전 범위는 보지 않는다.
///              대기 위치에서 engageEnterRadius 안에 들어오면 Engage로.
///   Engage   — stalkSpeedRatio로 천천히 대기 위치를 따라다니며, 플레이어가 교전 범위(적 중심의 원) 안에
///              머문 시간을 센다. engageDuration을 채우면 Rush로. 범위 밖으로 나가면 Approach로 돌아간다.
///              플레이어가 공격 중이면 시간을 멈추고 기다렸다가, 그 공격이 끝나는 순간 곧바로 Rush로(빈틈 공략).
///              플레이어가 띄워졌거나 쓰러져 있으면 시간을 0으로 묶고 지켜보기만 한다(일어선 뒤 처음부터 센다).
///   Rush     — 공격 위치(사거리 안쪽)로 달려간다. 사거리에 들어오는 순간 바로 친다.
///              돌진 중에 플레이어가 공격을 시작하거나 쓰러져도 멈추지 않는다 — 이미 출발한 돌진이다.
///              rushTimeout 안에 못 닿으면 포기하고 Approach로.
///   Attack   — 공격 동작이 끝날 때까지 이동 입력을 넣지 않는다(Lunge 속도가 지워지지 않게).
///              타격이 맞았을 때만 콤보 다음 단계를 잇는다(히트 확인). 빗나가면 그 단계로 끝난다.
///   Recover  — 공격권을 쥔 채 제자리에서 숨을 고른다. 플레이어가 반격할 틈이다.
///              그 사이 같은 쪽 Hold 적이 나보다 플레이어에 가까워지면 공격권을 넘긴다.
///              recoverDuration이 지나면, 아직 쥐고 있으면 Approach로(또 친다), 넘겼으면 Hold로.
///
/// 협공: 공격권은 플레이어의 좌·우에 하나씩, 둘뿐이다. 적은 자기가 선 쪽의 공격권만 가져갈 수 있다.
/// 공격권을 가진 적만 Approach~Attack을 돌고, 자리도 공격권 쪽에 잡는다.
/// 공격권은 공격할 때마다 돌아가며 넘어가지 않는다. 쥔 적이 띄워지거나 쓰러졌을 때, 또는 숨 고르는 사이
/// 대기하던 적이 더 가까워졌을 때만 넘어간다 — 기회가 생긴 적이 들어오는 것처럼 보이게.
/// 공격권 쪽 자리가 맵 밖이면(플레이어가 벽에 붙으면) 그 공격권은 아무도 쓸 수 없다.
/// 공격권은 별도 매니저 없이 이 클래스의 static으로 관리한다(적이 1종이라 따로 뺄 이유가 아직 없다).
/// 캐릭터끼리 몸 충돌이 없어서, 움직일 때는 가까운 적에게서 멀어지는 방향을 더해 서로 비켜 간다(separationRadius).
/// 서 있을 때는 그 절반 안까지 붙었을 때만 비켜선다.
///
/// 판단 범위(교전 범위)와 공격 사거리를 나눠둔 이유: 사거리 안에서만 시간을 세면 조금만 움직여도
/// 초기화되어 피하기가 너무 쉽다. 넓은 범위에서 재고, 칠 때는 직접 달려들어 거리를 좁힌다.
///
/// 맞는 중(경직·띄워짐·쓰러짐)엔 판단을 멈춘다. 단순 경직은 공격권을 지키고, 띄워지거나 쓰러지면 내놓는다.
/// 풀리면 공격권이 있으면 Approach, 없으면 Hold부터 다시 한다. 감지 전에 맞아도 쫓아온다.
///
/// 판단에는 랜덤 요소가 없다. 같은 상황이면 같은 행동을 한다. 예외는 Hold 대기 적의 버릇뿐이다 —
/// 불감대·반응 시간은 Awake에서 적마다 한 번 정하고, 잔동작의 간격·방향은 매번 정한다.
/// 판단 과정을 눈으로 보려면 같은 오브젝트에 EnemyBrainGizmo를 붙인다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 MovementCore + AttackCore + StateMachine, 계층 위쪽에 Character.
///          target에 플레이어의 Character, attackSkill에 이 적이 쓸 공격.
/// </summary>
public class EnemyBrain : MonoBehaviour
{
    /// <summary>AI의 판단 상태. EnemyBrainGizmo가 표시하려고 읽으므로 public이다.</summary>
    public enum AIState
    {
        Idle,
        Hold,
        Approach,
        Engage,
        Rush,
        Attack,
        Recover,
    }

    /// <summary>Hold 안에서의 세부 움직임. 대기 적들이 플레이어에 맞춰 일제히 움직이지 않게 나눠둔다.</summary>
    public enum HoldPhase
    {
        /// <summary>자리에 서 있다. 잔동작 간격을 센다.</summary>
        Settled,

        /// <summary>자리가 불감대 밖으로 벗어난 걸 알아챘다. 반응 시간이 지나면 움직인다.</summary>
        Reacting,

        /// <summary>자리로 걸어가는 중. 도착하면 Settled.</summary>
        Moving,

        /// <summary>자리 근처의 작은 지점으로 반 걸음 옮기는 중. 도착하면 Settled.</summary>
        Fidget,
    }

    /// <summary>목표 지점에 이만큼 가까우면 도착한 것으로 보고 멈춘다(m). 달리기 한 프레임 이동량보다 커야 지나치지 않는다.</summary>
    public const float ArriveRadius = 0.2f;

    /// <summary>
    /// 돌진할 때 노리는 공격 위치를 사거리 × 이 값만큼 떨어진 곳에 잡는다. 사거리 끝에 딱 맞추면
    /// 그 사이 플레이어가 조금만 물러나도 헛치기 때문에 안쪽으로 당긴다.
    /// </summary>
    const float StandDistanceRatio = 0.7f;

    /// <summary>켜져 있는 모든 적. Hold 중 '내가 가장 가까운가'를 가리는 데 쓴다.</summary>
    static readonly List<EnemyBrain> active = new List<EnemyBrain>();

    /// <summary>플레이어 왼쪽 공격권을 쥔 적. 없으면 null.</summary>
    static EnemyBrain leftHolder;

    /// <summary>플레이어 오른쪽 공격권을 쥔 적. 없으면 null.</summary>
    static EnemyBrain rightHolder;

    /// <summary>
    /// Play를 시작할 때마다 static을 비운다. Enter Play Mode 설정에서 Domain Reload를 끄면
    /// static이 이전 Play의 값(이미 사라진 적)을 그대로 들고 시작하기 때문이다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        active.Clear();
        leftHolder = null;
        rightHolder = null;
    }

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
             "남은 거리는 Engage의 느린 속도로 마저 좁힌다. Hold에서도 이 안에 들어오면 느리게 걷는다.")]
    [Min(ArriveRadius)]
    [SerializeField] private float engageEnterRadius = 1f;

    [Tooltip("교전 범위가 대기 거리보다 얼마나 넓은지(m). 교전 범위 = 대기 거리 + 이 값. " +
             "0보다 커야 대기 위치에 선 채로 시간이 흐른다.")]
    [Min(0.01f)]
    [SerializeField] private float engageMargin = 1f;

    [Tooltip("Engage 중 플레이어가 교전 범위 안에 이 시간(초) 동안 머물면 돌진한다.")]
    [FormerlySerializedAs("readyDuration")]
    [SerializeField] private float engageDuration = 0.4f;

    [Tooltip("Engage·Hold 중 따라다닐 때의 속도 배율. 1이면 보통 걷기, 0.5면 절반 속도.")]
    [Range(0f, 1f)]
    [SerializeField] private float stalkSpeedRatio = 0.5f;

    [Tooltip("돌진해서 이 시간(초) 안에 사거리에 못 들어가면 포기하고 다시 대기로 돌아간다.")]
    [SerializeField] private float rushTimeout = 1f;

    [Tooltip("공격이 끝난 뒤 제자리에서 숨을 고르는 시간(초). 플레이어가 반격할 틈이다.")]
    [SerializeField] private float recoverDuration = 0.8f;

    [Header("협공")]
    [Tooltip("공격권이 없는 적(Hold)이 대기 위치보다 얼마나 더 바깥에 서는지(m).")]
    [SerializeField] private float outerMargin = 2f;

    [Tooltip("공격권이 없는 적(Hold)이 플레이어 라인에서 위나 아래로 비껴 서는 거리(m). 순번이 짝수면 아래, 홀수면 위.")]
    [SerializeField] private float zSpread = 1.5f;

    [Tooltip("같은 쪽 Hold 적이 셋 이상일 때, 두 마리마다 이만큼(m) 더 바깥 줄에 선다.")]
    [Min(0f)]
    [SerializeField] private float holdSpacing = 1.5f;

    [Tooltip("다른 적과 이 거리(m) 안이면 이동 중에 서로 멀어지려 한다. 캐릭터끼리 몸 충돌이 없어서 이게 없으면 겹친다. " +
             "서 있을 때는 이 거리의 절반 안으로 들어왔을 때만 비켜선다.")]
    [Min(0.01f)]
    [SerializeField] private float separationRadius = 1.2f;

    [Tooltip("이동 중 밀어내기의 세기. 클수록 목표로 가는 것보다 간격 유지를 우선한다.")]
    [Min(0f)]
    [SerializeField] private float separationWeight = 1f;

    [Header("Hold 움직임")]
    [Tooltip("불감대 최솟값(m). 대기 자리가 지금 위치에서 불감대보다 멀어져야 따라 움직인다. " +
             "적마다 최솟값~최댓값 사이에서 한 번 정해서, 플레이어가 움직여도 대기 적들이 일제히 따라오지 않는다.")]
    [Min(0.01f)]
    [SerializeField] private float holdLeashMin = 0.8f;

    [Tooltip("불감대 최댓값(m).")]
    [SerializeField] private float holdLeashMax = 2f;

    [Tooltip("반응 시간 최솟값(초). 대기 자리가 불감대를 벗어난 걸 알아챈 뒤 이만큼 있다가 출발한다. 적마다 한 번 정한다.")]
    [Min(0f)]
    [SerializeField] private float reactionDelayMin = 0.15f;

    [Tooltip("반응 시간 최댓값(초).")]
    [SerializeField] private float reactionDelayMax = 0.5f;

    [Tooltip("잔동작 간격 최솟값(초). 자리에 서 있으면 이 간격마다 반 걸음씩 옮긴다. 간격은 매번 새로 정한다.")]
    [Min(0.1f)]
    [SerializeField] private float fidgetIntervalMin = 2f;

    [Tooltip("잔동작 간격 최댓값(초).")]
    [SerializeField] private float fidgetIntervalMax = 5f;

    [Tooltip("잔동작으로 자리에서 옮기는 최대 거리(m). 잔동작 때문에 추적이 시작되지 않도록 불감대 최솟값의 80%를 넘지 못한다.")]
    [Min(0f)]
    [SerializeField] private float fidgetDistance = 0.4f;

    MovementCore movement;
    AttackCore attack;
    StateMachine state;

    /// <summary>플레이어의 AttackCore. 휘두르는 중인지 보고 빈틈을 노리는 데 쓴다. 없으면 null이고 빈틈 공략을 건너뛴다.</summary>
    AttackCore targetAttack;

    /// <summary>플레이어의 StateMachine. 띄워졌거나 쓰러져 있는 동안 Engage를 멈추는 데 쓴다. 없으면 null이고 그 처리를 건너뛴다.</summary>
    StateMachine targetState;

    /// <summary>실제로 움직이는 몸. Character의 transform이다.</summary>
    Transform body;

    /// <summary>MapBounds가 없으면 null. 목표 지점이 맵 밖에 잡히지 않게 하는 데 쓴다.</summary>
    MapBounds map;

    AIState aiState = AIState.Idle;

    /// <summary>쥐고 있는 공격권. -1이면 왼쪽, 1이면 오른쪽, 0이면 없음.</summary>
    int tokenSide;

    /// <summary>맞는 중이라 판단을 멈춘 프레임인지.</summary>
    bool stunned;

    /// <summary>Engage 중 플레이어가 교전 범위 안에 머문 시간.</summary>
    float engageTimer;

    /// <summary>Engage 중 플레이어의 공격을 보고 기다리는 중인지. 그 공격이 끝나는 순간 돌진한다.</summary>
    bool waitingForOpening;

    // ── Hold 움직임

    HoldPhase holdPhase = HoldPhase.Settled;

    /// <summary>이 적의 불감대(m). Awake에서 한 번 정하고 바뀌지 않는다.</summary>
    float holdLeash;

    /// <summary>이 적의 반응 시간(초). Awake에서 한 번 정하고 바뀌지 않는다.</summary>
    float reactionDelay;

    /// <summary>Reacting에서 출발까지 남은 시간.</summary>
    float reactionTimer;

    /// <summary>Settled에서 다음 잔동작까지 남은 시간.</summary>
    float fidgetTimer;

    /// <summary>지금 서 있으려는 지점이 대기 자리에서 얼마나 비껴 있는지. 잔동작이 바꾸고, 자리로 이동할 때 0이 된다.</summary>
    Vector3 fidgetOffset;

    /// <summary>마지막으로 계산한 대기 자리. 불감대는 이 점을 기준으로 잰다.</summary>
    Vector3 holdSlot;

    /// <summary>Rush·Recover에서 남은 시간. 두 상태가 겹칠 일이 없어 하나로 같이 쓴다.</summary>
    float stateTimer;

    /// <summary>마지막으로 향한 목표 지점(바깥 대기 위치, 대기 위치 또는 공격 위치).</summary>
    Vector3 goal;

    // ── 읽기 전용 정보. EnemyBrainGizmo가 판단 과정을 그리는 데 쓴다.

    /// <summary>지금 판단 상태.</summary>
    public AIState State => aiState;

    /// <summary>맞는 중이라 판단을 멈췄는지. 이때 State는 풀린 뒤 돌아갈 상태(Approach 또는 Hold)다.</summary>
    public bool IsStunned => stunned;

    /// <summary>쥐고 있는 공격권. -1이면 왼쪽, 1이면 오른쪽, 0이면 없음.</summary>
    public int TokenSide => tokenSide;

    /// <summary>Engage 중 교전 범위 안에 머문 시간.</summary>
    public float EngageTimer => engageTimer;

    public float EngageDuration => engageDuration;

    /// <summary>Engage 중 플레이어가 공격하는 걸 보고 빈틈을 기다리는 중인지.</summary>
    public bool IsWaitingForOpening => waitingForOpening;

    /// <summary>Hold 안에서의 세부 움직임.</summary>
    public HoldPhase CurrentHoldPhase => holdPhase;

    /// <summary>이 적의 불감대(m).</summary>
    public float HoldLeash => holdLeash;

    /// <summary>이 적의 반응 시간(초).</summary>
    public float ReactionDelay => reactionDelay;

    /// <summary>Reacting에서 출발까지 남은 시간.</summary>
    public float ReactionTimer => reactionTimer;

    /// <summary>마지막으로 계산한 대기 자리. 불감대 원의 중심이다.</summary>
    public Vector3 HoldSlot => holdSlot;

    /// <summary>이동 중 밀어내기 거리. 서 있을 때는 이 절반이 기준이다.</summary>
    public float SeparationRadius => separationRadius;

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

    /// <summary>지금 목표 지점을 향해 움직이는 상태인지(Hold·Approach·Engage·Rush). 아니면 Goal은 지난 값이다.</summary>
    public bool HasGoal => aiState == AIState.Hold || aiState == AIState.Approach
                           || aiState == AIState.Engage || aiState == AIState.Rush;

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

        targetState = target.GetComponentInChildren<StateMachine>();
        if (targetState == null)
            Debug.LogWarning($"{name}: target 아래에서 StateMachine을 찾지 못해 '플레이어가 누워 있으면 Engage 멈춤'을 건너뜁니다.", this);

        map = MapBounds.Instance;

        // 대기 적의 성격. 한 번만 정해서, 각 적은 끝까지 같은 버릇으로 움직인다.
        holdLeash = Random.Range(holdLeashMin, holdLeashMax);
        reactionDelay = Random.Range(reactionDelayMin, reactionDelayMax);
        fidgetTimer = Random.Range(fidgetIntervalMin, fidgetIntervalMax);
    }

    // Awake에서 스스로 꺼졌으면(준비물 누락) OnEnable이 불리지 않아 목록에 들어가지 않는다.
    void OnEnable()
    {
        active.Add(this);
    }

    // 꺼지거나 파괴되면 쥐고 있던 공격권을 내놓는다. 안 그러면 그 자리가 영영 비지 않는다.
    void OnDisable()
    {
        active.Remove(this);
        ReleaseToken();
    }

    void Update()
    {
        // 경직·띄워짐·쓰러짐 중엔 아무것도 하지 않는다. SetMoveInput을 부르면 넉백 속도가 지워진다.
        // CanAct를 쓰지 않는 이유: 공격 중에도 false라서, 그걸로 막으면 Attack 상태가 공격이 끝난 걸 보지 못한다.
        bool knockedDown = state.State == CharacterState.Airborne || state.IsDown;
        stunned = knockedDown || state.State == CharacterState.Hit;
        if (stunned)
        {
            // 단순 경직으로는 자리를 뺏기지 않는다. 띄워지거나 쓰러지면 대기하던 적이 들어오도록 내놓는다.
            if (knockedDown)
                ReleaseToken();

            // 풀린 뒤엔 무슨 상태였든 다시 자리를 잡는다 — 하던 공격은 TakeHit이 이미 끊었고, 자리도 밀려났다.
            // 공격권이 없으면 Hold부터 — 내 쪽 공격권이 아직 비어 있고 내가 가장 가까우면 곧바로 되찾는다.
            aiState = tokenSide != 0 ? AIState.Approach : AIState.Hold;
            return;
        }

        switch (aiState)
        {
            case AIState.Idle:
                if (FlatDistance(body.position, target.transform.position) <= detectRange)
                    aiState = AIState.Hold;   // 빈 공격권이 있으면 Hold가 바로 가져간다
                break;

            case AIState.Hold:
                Hold();
                break;

            case AIState.Approach:
                if (LoseTokenIfSideBlocked()) break;

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
                if (LoseTokenIfSideBlocked()) break;
                Engage();
                break;

            case AIState.Rush:
                Rush();
                break;

            case AIState.Attack:
                // state.State가 아니라 IsAttacking으로 본다. 상태는 한 프레임 늦게 바뀔 수 있다.
                if (!attack.IsAttacking)
                {
                    // 공격권은 쥔 채로 숨을 고른다. 넘어가는지는 Recover가 정한다.
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
                StandStill();

                // 숨 고르는 사이 같은 쪽 대기 적이 나보다 플레이어에 가까워지면(플레이어가 피하며 그쪽으로 갔다든지)
                // 공격권을 내놓는다. 그 적이 다음 프레임 Hold에서 가져간다.
                if (tokenSide != 0 && WaitingEnemyCloser())
                    ReleaseToken();

                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    // 아직 쥐고 있으면 다시 들어가 또 친다. 넘겼으면 바깥으로 빠진다.
                    aiState = tokenSide != 0 ? AIState.Approach : AIState.Hold;
                }
                break;
        }
    }

    /// <summary>
    /// 공격권 없이 바깥 링에서 순서를 기다린다. 공격권을 얻으면 곧바로 Approach로 넘어간다.
    ///
    /// 대기 자리를 매 프레임 쫓으면 플레이어가 한 걸음만 움직여도 대기 적 전원이 같은 순간 출발·정지해서
    /// 줄 맞춰 끌려다니는 것처럼 보인다. 그래서:
    ///   불감대 — 자리가 이 적의 holdLeash보다 멀어져야 움직인다.
    ///   반응   — 벗어난 걸 알아챈 뒤 이 적의 reactionDelay만큼 있다가 출발한다. 그 사이 돌아오면 취소.
    ///   이동   — 일단 출발하면 자리까지 간다. 멀면 보통 속도(멀면 달리기), 가까우면 느리게.
    ///   잔동작 — 서 있는 동안 가끔 자리 근처로 반 걸음 옮긴다.
    /// </summary>
    void Hold()
    {
        if (TryClaimAsClosest())
        {
            aiState = AIState.Approach;
            return;
        }

        holdSlot = HoldPosition();
        float offSlot = FlatDistance(body.position, holdSlot);

        switch (holdPhase)
        {
            case HoldPhase.Settled:
            case HoldPhase.Fidget:
                if (offSlot > holdLeash)
                {
                    holdPhase = HoldPhase.Reacting;
                    reactionTimer = reactionDelay;
                }
                else if (holdPhase == HoldPhase.Settled)
                {
                    fidgetTimer -= Time.deltaTime;
                    if (fidgetTimer <= 0f)
                        StartFidget();
                }
                break;

            case HoldPhase.Reacting:
                if (offSlot <= holdLeash)
                {
                    holdPhase = HoldPhase.Settled;   // 플레이어가 잠깐 움직였다 돌아왔다. 출발 취소.
                    break;
                }

                reactionTimer -= Time.deltaTime;
                if (reactionTimer <= 0f)
                {
                    holdPhase = HoldPhase.Moving;
                    fidgetOffset = Vector3.zero;   // 새 자리로 갈 땐 잔동작으로 비낀 걸 버린다
                }
                break;
        }

        switch (holdPhase)
        {
            case HoldPhase.Moving:
                bool near = offSlot <= engageEnterRadius;
                if (MoveTo(holdSlot, near ? stalkSpeedRatio : 1f, near ? float.PositiveInfinity : runDistance))
                    holdPhase = HoldPhase.Settled;
                break;

            case HoldPhase.Fidget:
                if (MoveTo(holdSlot + fidgetOffset, stalkSpeedRatio, float.PositiveInfinity))
                    holdPhase = HoldPhase.Settled;
                break;

            default:
                // Settled·Reacting: 제자리에 서서 플레이어를 바라본다. 목표 지점은 표시용으로만 갱신한다.
                goal = holdSlot + fidgetOffset;
                StandStill();
                break;
        }
    }

    /// <summary>
    /// 자리 근처의 새 지점을 골라 반 걸음 옮기기 시작한다. 다음 잔동작까지의 간격도 새로 정한다.
    /// 옮기는 거리는 불감대 최솟값의 80%로 묶는다 — 잔동작이 불감대를 넘으면 그 자체로 추적이 시작된다.
    /// </summary>
    void StartFidget()
    {
        float maxDistance = Mathf.Min(fidgetDistance, holdLeashMin * 0.8f);
        Vector2 r = Random.insideUnitCircle * maxDistance;
        fidgetOffset = new Vector3(r.x, 0f, r.y);

        holdPhase = HoldPhase.Fidget;
        fidgetTimer = Random.Range(fidgetIntervalMin, fidgetIntervalMax);
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

        // 플레이어가 띄워졌거나 쓰러져 있는 동안은 덤비지 않고 지켜보기만 한다. 시간도 0으로 묶어둬서,
        // 일어선 뒤 engageDuration을 처음부터 다시 채워야 돌진한다 — 일어나자마자 맞는 일이 없게.
        // 이미 출발한 돌진은 여기를 지나지 않으므로 그대로 간다.
        bool targetDown = targetState != null
                          && (targetState.State == CharacterState.Airborne || targetState.IsDown);
        if (targetDown)
        {
            engageTimer = 0f;
            waitingForOpening = false;
            MoveTo(SlotPosition(StandoffDistance), stalkSpeedRatio, float.PositiveInfinity);
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

    // ── 공격권

    static EnemyBrain Holder(int side) => side < 0 ? leftHolder : rightHolder;

    static void SetHolder(int side, EnemyBrain brain)
    {
        if (side < 0) leftHolder = brain;
        else rightHolder = brain;
    }

    /// <summary>
    /// 그쪽 공격권을 쓸 수 있는지. 그쪽 대기 위치가 맵 밖이면(플레이어가 벽에 붙어 있으면) 쓸 수 없다 —
    /// 그 자리에 갈 수 없는 적이 공격권을 쥐고 있으면 그쪽 공격이 통째로 멈춘다.
    /// </summary>
    bool SideAvailable(int side)
    {
        if (map == null) return true;

        float x = target.transform.position.x + side * StandoffDistance;
        return x >= map.Bounds.min.x && x <= map.Bounds.max.x;
    }

    /// <summary>그쪽 공격권이 비어 있고 쓸 수 있으면 가져간다.</summary>
    bool TryClaimToken(int side)
    {
        if (Holder(side) != null || !SideAvailable(side)) return false;

        SetHolder(side, this);
        tokenSide = side;
        return true;
    }

    /// <summary>쥐고 있는 공격권을 내놓는다. 쥔 게 없으면 아무것도 하지 않는다.</summary>
    void ReleaseToken()
    {
        if (tokenSide == 0) return;

        if (Holder(tokenSide) == this)
            SetHolder(tokenSide, null);
        tokenSide = 0;
    }

    /// <summary>플레이어 기준으로 pos가 어느 쪽인지. 오른쪽(같은 X 포함)이면 1, 왼쪽이면 -1.</summary>
    int SideOf(Vector3 pos) => pos.x >= target.transform.position.x ? 1 : -1;

    /// <summary>
    /// 내가 서 있는 쪽의 공격권이 비어 있고, 그쪽에서 Hold 중인(맞는 중 제외) 적 가운데
    /// 내가 플레이어와 가장 가까우면 가져간다. 반대편 공격권은 비어 있어도 넘보지 않는다 —
    /// 적이 한쪽에만 몰려 있으면 그쪽에서만 공격한다.
    /// 거리가 똑같으면 둘 다 자기가 가장 가깝다고 보지만, 먼저 판단한 쪽이 가져가고 다른 쪽은
    /// TryClaimToken에서 실패하므로 한쪽 공격권이 둘에게 가지는 않는다.
    /// </summary>
    bool TryClaimAsClosest()
    {
        int mySide = SideOf(body.position);
        if (Holder(mySide) != null || !SideAvailable(mySide)) return false;

        float myDist = FlatDistance(body.position, target.transform.position);

        // List<T>의 foreach는 구조체 열거자를 써서 할당이 생기지 않는다.
        foreach (EnemyBrain other in active)
        {
            if (other == this || other.aiState != AIState.Hold || other.stunned) continue;
            if (SideOf(other.body.position) != mySide) continue;
            if (FlatDistance(other.body.position, target.transform.position) < myDist) return false;
        }

        return TryClaimToken(mySide);
    }

    /// <summary>
    /// 내 공격권 쪽에서 Hold 중인(맞는 중 제외) 적 가운데 나보다 플레이어에 가까운 적이 있는지.
    /// 공격을 마치고 숨 고르는 동안 공격권을 넘길지 정하는 데 쓴다.
    /// </summary>
    bool WaitingEnemyCloser()
    {
        float myDist = FlatDistance(body.position, target.transform.position);

        foreach (EnemyBrain other in active)
        {
            if (other == this || other.aiState != AIState.Hold || other.stunned) continue;
            if (SideOf(other.body.position) != tokenSide) continue;
            if (FlatDistance(other.body.position, target.transform.position) < myDist) return true;
        }

        return false;
    }

    /// <summary>
    /// 공격권 쪽 자리가 맵 밖으로 밀려났으면(플레이어가 벽에 붙었으면) 공격권을 내놓고 Hold로 간다.
    /// Approach·Engage에서만 본다 — 이미 달려드는 중(Rush)이거나 휘두르는 중이면 끝까지 간다.
    /// </summary>
    bool LoseTokenIfSideBlocked()
    {
        if (SideAvailable(tokenSide)) return false;

        ReleaseToken();
        aiState = AIState.Hold;
        return true;
    }

    // ── 이동

    /// <summary>
    /// 목표 지점으로 걷거나 달린다. 도착했으면 선다. 어느 쪽이든 늘 플레이어를 바라본다 —
    /// 물러날 때도 등을 보이지 않는다.
    ///
    /// 가는 동안 가까운 다른 적에게서 멀어지는 방향을 더해서, 서로 뚫고 지나가지 않고 비켜 간다.
    /// 더한 뒤 크기를 speedScale로 묶는다 — 밀려나는 힘 때문에 천천히 걷던 적이 빨라지면 안 된다.
    /// 달리기·바라보는 방향은 밀어내기와 상관없이 목표 기준으로 정한다.
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
            StandStill();
            return true;
        }

        // 뒷걸음질(플레이어 반대쪽으로 가는 중)엔 달리지 않는다. 플레이어를 보며 뒤로 달리면 문워크가 된다.
        bool towardTarget = toGoal.x * (target.transform.position.x - pos.x) > 0f;
        bool run = towardTarget && Mathf.Abs(toGoal.x) > runThreshold;

        Vector2 input = toGoal.normalized * speedScale + Separation(separationRadius) * separationWeight;
        movement.SetMoveInput(Vector2.ClampMagnitude(input, speedScale), run);

        // SetMoveInput이 입력 방향으로 돌려놓은 걸 플레이어 쪽으로 다시 돌린다. 같은 프레임 안이라 깜빡이지 않는다.
        movement.FaceTowards(target.transform.position.x);
        return false;
    }

    /// <summary>
    /// 서서 플레이어를 바라본다. 입력을 아예 안 넣으면 남은 속도가 마찰로 서서히 죽으며 미끄러지므로 0을 넣어 바로 세운다.
    ///
    /// 다만 다른 적이 separationRadius의 절반 안까지 들어와 있으면 느린 걸음으로 비켜선다.
    /// 보통 간격에선 가만히 있는다 — 서 있는 적까지 늘 밀어내면 자리 근처에서 미세하게 계속 걸어 떨려 보인다.
    /// </summary>
    void StandStill()
    {
        Vector2 push = Separation(separationRadius * 0.5f);
        movement.SetMoveInput(push.sqrMagnitude > 0.0001f ? push.normalized * stalkSpeedRatio : Vector2.zero);
        movement.FaceTowards(target.transform.position.x);
    }

    /// <summary>
    /// radius 안에 있는 다른 적들에게서 멀어지는 방향의 합. 가까울수록 세다(경계에서 0, 딱 붙으면 1).
    /// 맞는 중이거나 Idle인 적도 몸이 있으니 피한다. 플레이어는 보지 않는다.
    /// 완전히 같은 자리에 겹쳐 있으면 방향을 정할 수 없어서, 인스턴스 ID 순서로 하나는 위(+Z), 하나는 아래로 민다.
    /// </summary>
    Vector2 Separation(float radius)
    {
        Vector3 pos = body.position;
        int myId = GetInstanceID();
        Vector2 push = Vector2.zero;

        foreach (EnemyBrain other in active)
        {
            if (other == this) continue;

            Vector3 o = other.body.position;
            Vector2 away = new Vector2(pos.x - o.x, pos.z - o.z);
            float d = away.magnitude;
            if (d >= radius) continue;

            Vector2 dir = d > 0.0001f ? away / d : new Vector2(0f, myId < other.GetInstanceID() ? 1f : -1f);
            push += dir * (1f - d / radius);
        }

        return push;
    }

    /// <summary>
    /// 플레이어 옆의 자리. 같은 라인(Z)에서, 공격권 쪽(좌/우)으로 distance만큼 떨어진 곳이다.
    /// 공격권이 있을 때만 부른다. 그쪽이 맵 밖인 경우는 LoseTokenIfSideBlocked가 미리 걸러낸다.
    /// </summary>
    Vector3 SlotPosition(float distance)
    {
        Vector3 t = target.transform.position;
        return new Vector3(t.x + tokenSide * distance, body.position.y, t.z);
    }

    /// <summary>
    /// 공격권이 없는 적의 바깥 대기 위치. 같은 쪽 Hold 적들 사이의 순번(HoldSlotIndex)으로 자리를 나눠
    /// 겹치지 않게 한다. 두 자리가 한 줄이다:
    ///   0번 아래, 1번 위 — 대기 거리 + outerMargin
    ///   2번 아래, 3번 위 — 거기서 holdSpacing만큼 더 바깥 …
    /// 위아래는 플레이어 라인에서 zSpread만큼 비낀다. 좌우든 위아래든 그쪽이 맵 밖이면 반대쪽을 잡는다.
    /// </summary>
    Vector3 HoldPosition()
    {
        Vector3 t = target.transform.position;
        Vector3 pos = body.position;

        int side = SideOf(pos);
        int slot = HoldSlotIndex(side);

        float sideX = side;
        float sideZ = slot % 2 == 0 ? -1f : 1f;
        float distance = StandoffDistance + outerMargin + (slot / 2) * holdSpacing;
        float x = t.x + sideX * distance;
        float z = t.z + sideZ * zSpread;

        if (map != null)
        {
            Bounds b = map.Bounds;
            if (x < b.min.x || x > b.max.x) x = t.x - sideX * distance;
            if (z < b.min.z || z > b.max.z) z = t.z - sideZ * zSpread;
        }

        return new Vector3(x, pos.y, z);
    }

    /// <summary>
    /// 같은 쪽 Hold 적들 사이에서 나의 순번. 플레이어에 가까울수록 앞(안쪽 자리)이다 —
    /// 공격권이 비면 들어가는 순서와 같다.
    ///
    /// 거리 차이가 holdSpacing의 절반 이내면 거리 대신 인스턴스 ID로 순서를 정한다. 위아래 한 쌍은
    /// 플레이어까지 거리가 똑같아서, 거리만 보면 미세한 흔들림에 순번이 매 프레임 뒤바뀌고
    /// 둘이 서로 자리를 바꾸려고 엇갈리며 떨게 된다.
    /// </summary>
    int HoldSlotIndex(int side)
    {
        Vector3 t = target.transform.position;
        float myDist = FlatDistance(body.position, t);
        float tieBand = holdSpacing * 0.5f;
        int myId = GetInstanceID();

        int index = 0;
        foreach (EnemyBrain other in active)
        {
            if (other == this || other.aiState != AIState.Hold) continue;
            if (SideOf(other.body.position) != side) continue;

            float diff = FlatDistance(other.body.position, t) - myDist;
            bool ahead = Mathf.Abs(diff) <= tieBand ? other.GetInstanceID() < myId : diff < 0f;
            if (ahead) index++;
        }

        return index;
    }

    /// <summary>높이를 뺀 바닥 평면(X·Z) 거리.</summary>
    static float FlatDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
