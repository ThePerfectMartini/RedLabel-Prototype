using UnityEngine;

/// <summary>
/// 공격 동작을 시작하고, 공격 클립의 Animation Event가 부르는 순간에 타격 판정을 수행하는 컴포넌트.
///
/// 클립 재생은 StateMachine이 맡는다 — 애니메이터의 주인이 둘이면 서로 덮어쓰기 때문이다.
/// 여기서는 attackTimer만 세우고, StateMachine이 그걸 보고 Attack 상태로 넘어가며 클립을 튼다.
///
/// 판정용 콜라이더를 만들지 않는다. Physics.OverlapBox는 "지금 이 자리에 이런 상자가 있다면
/// 뭐가 겹치나"를 물어보는 일회성 조회라, 부른 줄에서 즉시 결과가 나오고 아무것도 남지 않는다.
/// 그래서 "칼이 내려오는 정확한 프레임"에 판정을 낼 수 있다.
///
/// 수치는 직접 갖지 않는다. 어떤 공격이냐에 따라 달라지므로 SkillData 에셋이 전부 들고 있고,
/// TryAttack을 부르는 쪽이 어느 스킬인지 건네준다.
///
/// 콤보도 여기서 굴린다. 공격 클립의 Animation Event가 콤보 창을 열기 전까지 들어온 입력은 버리고
/// (OnComboWindowOpen 이벤트가 없는 클립은 타격 프레임에 열린다),
/// 창이 열린 뒤의 입력은 남은 동작을 끊고 즉시 다음 단계를 시작한다. 어느 공격이 어느 공격으로
/// 이어지는지는 SkillData의 nextCombo가 쥐고 있어서, 이 스크립트는 콤보가 몇 단인지 알지 못한다.
///
/// 다른 키의 입력은 원래 버리지만, cancelsBasicAttack이 켜진 스킬은 지상 기본공격 콤보를
/// 언제든 끊고 곧바로 시작한다(예: X 도중의 Z).
///
/// 흐름:  X·Z키 → TryAttack(스킬) → (StateMachine이 그 스킬의 클립 재생) → (타격 프레임) Animation Event
///        → AnimationEventRelay.OnAttackHitFrame() → ExecuteHit() → OverlapBox
///
/// [준비물] 같은 오브젝트에 MovementCore + StateMachine. 계층 위쪽에 Character,
///          그 아래 어딘가에 Animator + AnimationEventRelay.
/// </summary>
[RequireComponent(typeof(MovementCore))]
public class AttackCore : MonoBehaviour
{
    /// <summary>
    /// 판정 기즈모를 보여주는 시간(초). 판정 자체는 한 순간이라 그냥은 눈에 안 보이기 때문에 남겨둔다.
    /// </summary>
    const float HitGizmoDuration = 0.3f;

    /// <summary>이 Core가 속한 캐릭터. 몸 transform을 여기서 받아간다.</summary>
    Character character;

    /// <summary>판정 위치의 기준이 되는 transform. Character의 것이다.</summary>
    Transform body;

    MovementCore movement;
    Animator animator;

    /// <summary>지금 쓰고 있는 스킬. 판정·효과 수치를 전부 여기서 읽는다.</summary>
    SkillData currentSkill;

    /// <summary>
    /// 지금 진행 중인 콤보를 시작한 스킬. 다른 공격키의 입력을 걸러내는 데 쓴다 —
    /// X 콤보가 도는 중에 Z를 눌렀다고 X의 다음 단계가 나가면 안 된다.
    /// </summary>
    SkillData comboRoot;

    /// <summary>
    /// 지금 동작을 끊고 다음 단계로 넘어가도 되는지. 공격 클립의 Animation Event가 켜고,
    /// 새 단계가 시작되거나 공격이 끊기면 꺼진다.
    /// </summary>
    bool comboWindowOpen;

    /// <summary>
    /// 지금 클립에 OnComboWindowOpen 이벤트가 박혀 있는지. 있으면 그 이벤트가, 없으면 타격 프레임이
    /// 콤보 창을 연다. 새 단계를 시작할 때 클립을 찾으면서 함께 확인한다.
    /// </summary>
    bool clipHasComboWindowEvent;

    /// <summary>남은 공격 시간. 0보다 크면 공격 동작 중이다.</summary>
    float attackTimer;

    /// <summary>
    /// 지금 동작을 공중에서 시작했는지. 그렇다면 착지하는 순간 동작을 끊는다 —
    /// 공중 공격 클립이 땅 위에서 끝까지 재생되면 착지한 뒤에도 허공을 휘두르는 것처럼 보이기 때문이다.
    /// </summary>
    bool startedAirborne;

    /// <summary>
    /// 공격 동작이 시작된 누적 횟수. 콤보 단계가 넘어가도 상태는 Attack 그대로라서,
    /// StateMachine이 '상태가 바뀌었나'만으로는 클립을 갈아야 할 때를 알 수 없다.
    /// 이 숫자가 달라졌다는 것이 곧 새 동작이 시작됐다는 뜻이다.
    /// </summary>
    int attackStartCount;

    /// <summary>
    /// 지금 동작의 타격 판정이 누군가를 맞혔는지. 판정이 나갈 때 기록하고, 새 동작(콤보 다음 단계 포함)이 시작되면 끈다.
    /// AI가 맞혔을 때만 콤보를 잇는 데 쓴다.
    /// </summary>
    bool hitLanded;

    // ── 기즈모 표시용. 판정이 일어난 '그 순간'의 상자를 기억한다.
    Vector3 lastHitCenter;
    Vector3 lastHitSize;
    bool lastHitAnything;
    float hitGizmoTimer;

    /// <summary>공격 동작 중인지. 입력 쪽이 이동·재공격을 막는 데 쓴다.</summary>
    public bool IsAttacking => attackTimer > 0f;

    /// <summary>지금 쓰고 있는 스킬. StateMachine이 재생할 클립 이름을 여기서 가져간다.</summary>
    public SkillData CurrentSkill => currentSkill;

    /// <summary>공격 동작이 시작된 누적 횟수. StateMachine이 클립을 다시 틀 때를 잡는 데 쓴다.</summary>
    public int AttackStartCount => attackStartCount;

    /// <summary>지금 동작의 타격이 누군가를 맞혔는지. 판정 전이거나 빗나갔으면 false다. AI가 히트 확인 콤보에 쓴다.</summary>
    public bool HitLanded => hitLanded;

    void Awake()
    {
        movement = GetComponent<MovementCore>();

        character = GetComponentInParent<Character>();
        if (character == null)
        {
            Debug.LogError($"{name}: 계층 위쪽에서 Character를 찾지 못했습니다. 공격을 끕니다.", this);
            enabled = false;
            return;
        }

        body = character.transform;

        // Animator는 모델이 들어있는 자식에 붙는다. 이 스크립트와 다른 가지일 수 있어 Character 기준으로 찾는다.
        animator = character.GetComponentInChildren<Animator>();
        if (animator == null)
            Debug.LogWarning($"{name}: Character 아래에서 Animator를 찾지 못했습니다. 공격 클립이 재생되지 않습니다.", this);
    }

    /// <summary>
    /// 주어진 이름의 클립을 컨트롤러에서 찾아온다. 동작 길이와 콤보 창 이벤트 유무를 여기서 읽는다.
    ///
    /// Animator.GetCurrentAnimatorStateInfo는 Play() 직후에도 아직 '이전 상태'를 돌려준다.
    /// 애니메이터가 다음에 평가될 때까지 갱신되지 않기 때문이다. 그래서 재생하는 자리에서
    /// 길이를 물어볼 수 없고, 컨트롤러가 들고 있는 클립 목록에서 직접 찾는다.
    ///
    /// 클립 이름이 상태 이름과 같다고 전제한다. 어느 한쪽 이름만 바꾸면 여기가 끊긴다.
    /// 공격을 시작할 때마다 부르지만, 초당 몇 번 수준이라 매 프레임 도는 경로가 아니다.
    /// </summary>
    AnimationClip FindClip(string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return null;

        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == clipName)
                return clip;
        }

        Debug.LogWarning($"{name}: 컨트롤러에서 \"{clipName}\" 클립을 찾지 못해 공격 시간을 0으로 둡니다.", this);
        return null;
    }

    /// <summary>클립에 OnComboWindowOpen 이벤트가 걸려 있는지.</summary>
    static bool HasComboWindowEvent(AnimationClip clip)
    {
        // clip.events는 부를 때마다 배열을 새로 만들어 돌려준다. 공격 시작 때 한 번만 부르므로 괜찮다.
        foreach (AnimationEvent evt in clip.events)
        {
            if (evt.functionName == nameof(AnimationEventRelay.OnComboWindowOpen))
                return true;
        }
        return false;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (attackTimer > 0f)
            attackTimer -= dt;

        // 공중 공격 중 착지. MovementCore와 Update 순서가 정해져 있지 않아 최대 한 프레임 늦을 수 있다.
        if (IsAttacking && startedAirborne && movement.IsGrounded)
            Cancel();

        if (hitGizmoTimer > 0f)
            hitGizmoTimer -= dt;
    }

    /// <summary>
    /// 공격 시작, 또는 콤보 다음 단계로 캔슬. 입력이나 AI가 어느 스킬을 쓸지 정해서 부른다.
    ///
    /// 쉬고 있을 때 부르면 그 스킬로 공격을 시작한다. 이미 동작 중에 부르면, 콤보 창이 열려 있을 때만
    /// 남은 동작을 끊고 곧바로 다음 단계를 시작한다. 창이 열리기 전의 입력은 버려진다.
    ///
    /// 새 단계가 시작되면 창이 다시 닫히므로, 연타해도 한 동작에 한 단계씩만 넘어간다.
    /// </summary>
    public void TryAttack(SkillData skill)
    {
        if (skill == null) return;

        if (IsAttacking)
        {
            // 진행 중인 콤보를 시작한 키가 아니면 무시한다. 다른 공격으로 갈아타는 건 콤보가 아니다.
            // 예외는 기본공격 캔슬 — 지상에서 시작한 기본공격 콤보는 그런 스킬에게 즉시 끊긴다.
            // 2·3단 중에도 comboRoot는 1단이라, basicAttack과 비교하면 콤보 전체가 걸린다.
            if (skill != comboRoot)
            {
                CharacterSettings settings = character.Settings;
                bool cancelBasic = skill.cancelsBasicAttack
                                   && !startedAirborne
                                   && settings != null
                                   && comboRoot == settings.basicAttack;

                if (cancelBasic)
                {
                    comboRoot = skill;
                    Begin(skill);
                }
                return;
            }

            // 클립의 콤보 창 이벤트를 아직 지나지 않았다.
            if (!comboWindowOpen) return;

            // 마지막 단계에서는 이어갈 곳이 없으니 입력이 그냥 버려진다.
            if (currentSkill == null || currentSkill.nextCombo == null) return;

            Begin(currentSkill.nextCombo);
            return;
        }

        comboRoot = skill;
        Begin(skill);
    }

    /// <summary>
    /// 한 단계를 실제로 시작한다. 콤보의 첫 단계든 이어지는 단계든 여기를 지난다.
    /// comboRoot는 건드리지 않는다 — 콤보가 진행되는 동안 '무슨 키로 시작했나'는 그대로여야 한다.
    /// </summary>
    void Begin(SkillData skill)
    {
        currentSkill = skill;
        comboWindowOpen = false;
        hitLanded = false;
        startedAirborne = !movement.IsGrounded;

        // 동작 길이는 그 스킬의 클립에서 그때그때 읽는다. 스킬마다 클립이 다르기 때문이다.
        // 클립을 못 찾으면 0이라 IsAttacking이 서지 않고 공격이 통째로 불발된다.
        AnimationClip clip = FindClip(skill.animatorStateName);
        attackTimer = clip != null ? clip.length : 0f;
        clipHasComboWindowEvent = clip != null && HasComboWindowEvent(clip);

        // 공격 타입에 따라 시전자를 민다. Locked Attack은 거리 0이라 걷던 속도를 끊고 그 자리에 선다.
        // 콤보 단계마다 여기를 지나므로, 각 단계가 자기 타입으로 속도를 새로 정한다.
        // Jump Attack은 시전자 이동을 건드리지 않는다. 공중 이동은 MovementCore의 공중 조작이 맡는다.
        if (skill.attackType != AttackType.JumpAttack)
        {
            float lungeDistance = skill.attackType == AttackType.LungeAttack ? skill.lungeDistance : 0f;
            movement.Lunge(lungeDistance);
        }

        attackStartCount++;
    }

    /// <summary>
    /// 진행 중인 공격을 통째로 끊는다. 맞았을 때 StateMachine이 부르고, 공중 공격 중 착지하면 스스로 부른다.
    ///
    /// 콤보 창만 닫고 동작을 남겨두면, 경직이 풀리는 순간 상태가 Hit에서 Attack으로
    /// 돌아가면서 StateMachine이 공격 클립을 0프레임부터 다시 틀어버린다. 그래서 타이머까지 끊는다.
    ///
    /// currentSkill은 남겨둔다 — 경직 중에는 Hit 클립이 도니 쓰이지 않고, 다음 공격이 어차피 덮어쓴다.
    /// </summary>
    public void Cancel()
    {
        attackTimer = 0f;
        comboWindowOpen = false;
        comboRoot = null;
    }

    /// <summary>
    /// 콤보 창을 연다. 이 뒤로 들어온 같은 공격키 입력은 남은 동작을 끊고 다음 단계로 넘어간다.
    /// 공격 클립의 Animation Event가 AnimationEventRelay를 거쳐 호출한다.
    ///
    /// 스킬의 선입력 타이밍 직접설정이 꺼져 있으면 열지 않는다. 켜져 있으면 클립에
    /// OnComboWindowOpen 이벤트가 있을 때는 그 이벤트로만, 없을 때는 타격 프레임으로만 연다.
    /// </summary>
    /// <param name="fromHitFrame">타격 프레임 이벤트에서 불렸으면 true.</param>
    public void OpenComboWindow(bool fromHitFrame)
    {
        if (currentSkill == null || !currentSkill.useComboInputTiming) return;

        bool isWindowTiming = clipHasComboWindowEvent ? !fromHitFrame : fromHitFrame;
        if (isWindowTiming)
            comboWindowOpen = true;
    }

    /// <summary>
    /// 실제 타격 판정. 앞쪽 상자 안에 있는 다른 캐릭터를 전부 찾는다.
    ///
    /// 공격 클립의 타격 프레임에 걸린 Animation Event가 AnimationEventRelay를 거쳐 호출한다.
    /// Animation Event는 public이고 매개변수가 0~1개인 함수만 부를 수 있어서 형태를 이렇게 고정했다.
    /// </summary>
    public void ExecuteHit()
    {
        if (currentSkill == null) return;

        // 판정 시점의 위치·방향으로 계산한다. 누른 순간이 아니라 칼이 닿는 순간 기준이다.
        Vector3 center = body.position + movement.FacingDir * currentSkill.attackDistance;
        Vector3 size = currentSkill.attackBoxSize;

        Collider[] hits = Physics.OverlapBox(
            center,
            size * 0.5f,                     // OverlapBox는 전체 크기가 아니라 절반 크기를 받는다
            Quaternion.identity,             // 좌우만 보므로 상자를 회전시킬 일이 없다
            ~0,                              // 모든 레이어. 나중에 대상 레이어만 거르려면 여기를 바꾼다
            QueryTriggerInteraction.Collide);

        int count = 0;
        foreach (Collider hit in hits)
        {
            // 콜라이더가 어느 자식에 붙어 있든, 위로 올라가며 그 콜라이더의 주인 캐릭터를 찾는다.
            Character target = hit.GetComponentInParent<Character>();

            if (target == null) continue;        // 바닥·배경 등 캐릭터가 아닌 것
            if (target == character) continue;   // 자기 자신

            // 넉백·에어본. 세기는 맞은 쪽이 아니라 때린 쪽 수치로 정한다.
            // 다만 지상용·공중용 중 어느 수치를 쓸지는 맞은 쪽 상태가 정한다 — 서 있는 상대와
            // 이미 떠 있는 상대(저글링)는 같은 공격이라도 원하는 결과가 다르기 때문이다.
            MovementCore targetMove = target.GetComponentInChildren<MovementCore>();

            // 이미 떠 있는 대상을 때렸거나 위로 띄웠는지. 맞은 쪽 StateMachine이 Airborne으로 갈지 정하는 데 쓴다.
            bool launched = false;

            if (targetMove != null)
            {
                bool targetAirborne = !targetMove.IsGrounded;

                float knockbackPower = targetAirborne
                    ? currentSkill.airborneKnockbackPower
                    : currentSkill.groundKnockbackPower;

                // 지상 런치는 '띄우는 공격'일 때만 쓴다. 꺼져 있으면 0이라 바닥으로만 밀린다.
                // 공중 런치는 그 체크와 무관하게 늘 쓰인다(0이면 수직에 손대지 않는다).
                float launchPower = targetAirborne
                    ? currentSkill.airborneLaunchPower
                    : (currentSkill.isLauncher ? currentSkill.groundLaunchPower : 0f);

                Vector3 knockVelocity = movement.FacingDir * knockbackPower;
                knockVelocity.y = launchPower;
                targetMove.Launch(knockVelocity);

                launched = targetAirborne || launchPower > 0f;
            }

            // 경직. 이 시간 동안 대상은 Hit 상태가 되어 이동·공격 입력이 막힌다. 띄웠으면 착지할 때까지 Airborne이다.
            StateMachine targetState = target.GetComponentInChildren<StateMachine>();
            if (targetState != null)
                targetState.TakeHit(currentSkill.hitStunDuration, launched);

            count++;

            // damage는 아직 아무도 소비하지 않는다. 체력 시스템이 생기기 전까지는 로그로만 확인한다.
            Debug.Log($"[공격] {character.name} → {target.name} 명중 ({currentSkill.name}, 데미지 {currentSkill.damage})", target.gameObject);
        }

        if (count == 0)
            Debug.Log($"[공격] {character.name} → 허공 ({currentSkill.name})", character.gameObject);

        // 한 동작에 타격 프레임이 여럿이어도, 한 번이라도 맞혔으면 명중으로 친다.
        if (count > 0)
            hitLanded = true;

        lastHitCenter = center;
        lastHitSize = size;
        lastHitAnything = count > 0;
        hitGizmoTimer = HitGizmoDuration;
    }

    // 판정이 나간 직후 HitGizmoDuration초 동안만 상자를 보여준다.
    void OnDrawGizmos()
    {
        if (hitGizmoTimer <= 0f) return;

        Gizmos.color = lastHitAnything ? Color.red : Color.gray;
        Gizmos.DrawWireCube(lastHitCenter, lastHitSize);
    }
}