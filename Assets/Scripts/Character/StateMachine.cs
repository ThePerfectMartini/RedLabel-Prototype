using UnityEngine;

/// <summary>캐릭터의 행동 상태. StateMachine.StateNames 배열과 순서가 1:1로 맞아야 한다.</summary>
public enum CharacterState
{
    Idle,
    Move,
    Attack,
    Hit,
    Run,
    JumpReady,
    JumpAir,
    Airborne,
    AirborneLanded,
    LieDown,
    GetUp,
}

/// <summary>
/// 캐릭터가 지금 무슨 상태인지 한곳에서 판정하고, 그 상태에 맞는 애니메이션을 재생한다.
///
/// 상태를 따로 들고 있지 않고 매 프레임 다시 판정한다 — MovementCore·AttackCore가 이미 답을
/// 갖고 있는 사실(지상인지, 공격 중인지)이라, 복사해 두면 둘이 어긋날 수 있기 때문이다.
/// 스스로 들고 있는 건 경직 타이머 하나뿐이다.
///
/// 우선순위: 띄워짐 > 쓰러짐 > 피격 > 공격 > 점프 준비 > 공중 > 달리기·이동 > 대기
///
/// 띄워짐(Airborne)은 경직 시간과 무관하게 착지할 때까지 간다.
/// 띄워진 채로 또 맞으면 피격 클립을 한 번 틀고 띄워짐 클립으로 돌아간다(상태는 계속 Airborne).
///
/// 착지하면 쓰러짐이 이어진다: 착지(AirborneLanded) 클립 끝까지 → 누워서(LieDown) lieDownDuration초 →
/// 일어서기(GetUp) 클립 끝까지. 그 뒤 경직이 남아 있으면 피격으로 넘어간다.
/// 쓰러져 있는 도중에 맞으면 쓰러짐은 거기서 끊기고 피격(띄웠으면 띄워짐)으로 간다.
///
/// 애니메이터의 주인은 이 컴포넌트 하나다. 다른 곳에서 animator.Play를 부르면 서로 덮어쓴다.
///
/// [준비물] 같은 오브젝트에 MovementCore + AttackCore.
///          계층 위쪽에 Character, 그 아래 어딘가에 Animator.
/// </summary>
[RequireComponent(typeof(MovementCore), typeof(AttackCore))]
public class StateMachine : MonoBehaviour
{
    /// <summary>걷기로 치는 최소 속도. 이보다 느리면 서 있는 것으로 본다.</summary>
    const float MoveSpeedThreshold = 0.01f;

    /// <summary>
    /// 상태별 Animator 상태 이름. CharacterState의 순서와 1:1로 맞아야 하고,
    /// 컨트롤러에 만들어 둔 상태 이름과도 정확히 일치해야 한다.
    ///
    /// Attack 칸은 스킬이 이름을 정하지 못했을 때의 대비책일 뿐이다 — 보통은 SkillData가 정한다.
    /// </summary>
    static readonly string[] StateNames = { "Idle", "Move", "Attack", "Hit", "Run", "JumpReady", "JumpAir", "Airborne", "Airborne Landed", "Lie Down", "Get Up" };

    /// <summary>공중 피격 클립이 끝났는지 볼 때 쓰는 Hit 상태의 해시. 매 프레임 문자열을 해시하지 않으려고 미리 구해둔다.</summary>
    static readonly int HitStateHash = Animator.StringToHash(StateNames[(int)CharacterState.Hit]);

    /// <summary>착지 클립이 끝났는지 볼 때 쓰는 해시.</summary>
    static readonly int AirborneLandedStateHash = Animator.StringToHash(StateNames[(int)CharacterState.AirborneLanded]);

    /// <summary>일어서기 클립이 끝났는지 볼 때 쓰는 해시.</summary>
    static readonly int GetUpStateHash = Animator.StringToHash(StateNames[(int)CharacterState.GetUp]);

    [Tooltip("띄워졌다 착지한 뒤 누워 있는 시간(초). 착지 클립이 끝난 뒤부터 센다.")]
    [SerializeField] private float lieDownDuration = 1f;

    MovementCore movement;
    AttackCore attack;
    Animator animator;

    /// <summary>남은 피격 경직 시간. 0보다 크면 얻어맞는 중이다.</summary>
    float hitTimer;

    /// <summary>
    /// 맞는 순간 켜지는 표시. 이미 Hit 상태일 때 또 맞아도 피격 클립을 처음부터 다시 틀기 위한 것이다.
    /// 이게 없으면 상태가 그대로라 Play가 불리지 않고, 연타해도 깜빡임이 한 번만 돈다.
    /// </summary>
    bool hitRetriggered;

    /// <summary>
    /// 마지막으로 클립을 튼 시점의 AttackCore.AttackStartCount.
    /// 콤보는 단계가 넘어가도 상태가 Attack에 머물러서, 상태만 보면 클립을 갈 때를 놓친다.
    /// 이 숫자가 어긋났다는 것이 곧 다음 단계가 시작됐다는 뜻이다.
    /// </summary>
    int playedAttackStartCount;

    /// <summary>
    /// 맞아서 떠 있는 중인지. 띄우는 타격을 받으면 켜지고, 착지하면 Evaluate가 끈다.
    /// 공중인지는 MovementCore만 봐선 모른다 — 지상 넉백도 한 프레임은 공중으로 잡히기 때문에,
    /// 띄웠는지는 때린 쪽이 TakeHit으로 알려준다.
    /// </summary>
    bool airborneHit;

    /// <summary>Airborne 상태에서 또 맞아 Hit 클립을 틀어둔 중인지. 끝나면 Airborne 클립으로 돌아간다.</summary>
    bool airHitPlaying;

    /// <summary>
    /// 착지 후 쓰러짐의 현재 단계(AirborneLanded / LieDown / GetUp). 쓰러져 있지 않으면 Idle.
    /// 단계 전환은 Evaluate가 클립 끝·누운 시간을 보고 한다.
    /// </summary>
    CharacterState downPhase = CharacterState.Idle;

    /// <summary>LieDown 단계에서 남은 누운 시간.</summary>
    float lieDownTimer;

    CharacterState state = CharacterState.Idle;

    /// <summary>지금 상태.</summary>
    public CharacterState State => state;

    /// <summary>착지 후 쓰러져 있는 중인지(착지·누움·일어서기). 이동·공격 입력을 막는 데 쓴다.</summary>
    public bool IsDown => state == CharacterState.AirborneLanded
                          || state == CharacterState.LieDown
                          || state == CharacterState.GetUp;

    /// <summary>
    /// 점프 입력을 받아도 되는지. 공격 동작 중이거나 경직 중이거나 맞아서 떠 있거나 쓰러져 있으면 안 된다.
    /// 공격키는 이걸 보지 않는다 — 공격 중의 입력도 콤보 선입력으로 받아야 하기 때문이다.
    /// </summary>
    public bool CanAct => state != CharacterState.Attack && state != CharacterState.Hit && state != CharacterState.Airborne && !IsDown;

    void Awake()
    {
        movement = GetComponent<MovementCore>();
        attack = GetComponent<AttackCore>();

        // Animator는 모델이 들어있는 자식에 붙는다. 이 스크립트와 다른 가지라 Character 기준으로 찾는다.
        Character character = GetComponentInParent<Character>();
        if (character != null)
            animator = character.GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning($"{name}: Animator를 찾지 못해 애니메이션을 재생할 수 없습니다.", this);
    }

    /// <summary>
    /// 맞았을 때 때린 쪽이 부른다. 이 시간 동안 Hit 상태가 되어 이동·공격 입력이 막힌다.
    /// 경직 중에 또 맞으면 새 값으로 덮어써서 그 시점부터 다시 센다.
    ///
    /// 내밀던 공격은 여기서 끊는다. 맞으면서도 콤보가 이어지면 안 되고, 남겨두면 경직이 풀릴 때
    /// 하던 공격이 처음부터 다시 재생된다. 경직이 풀린 뒤에는 콤보 1단부터 새로 시작한다.
    /// </summary>
    /// <param name="launched">공중으로 띄웠거나 이미 공중에 있는 대상을 때렸으면 true. 착지할 때까지 Airborne이 된다.</param>
    public void TakeHit(float stunDuration, bool launched)
    {
        hitTimer = stunDuration;
        hitRetriggered = true;
        airborneHit = launched;

        // 쓰러져 있다가 맞으면 쓰러짐은 끊는다. 피격(띄웠으면 띄워짐)이 이어받는다.
        downPhase = CharacterState.Idle;

        attack.Cancel();
    }

    void Update()
    {
        if (hitTimer > 0f)
            hitTimer -= Time.deltaTime;

        CharacterState next = Evaluate();

        // 착지 등으로 상태가 바뀌면 공중 피격 클립은 거기서 끝난다. 새 상태의 클립은 아래에서 튼다.
        if (next != state)
            airHitPlaying = false;

        // 이미 떠 있는 채로 또 맞았다. 상태는 Airborne 그대로 두고 클립만 Hit으로 바꾼다.
        if (hitRetriggered && state == CharacterState.Airborne && next == CharacterState.Airborne)
        {
            hitRetriggered = false;
            airHitPlaying = true;
            PlayClip(StateNames[(int)CharacterState.Hit]);
            return;
        }

        // 공중 피격 클립이 한 바퀴 돌았으면 Airborne 클립으로 돌아간다.
        if (airHitPlaying && ClipFinished(HitStateHash))
        {
            airHitPlaying = false;
            PlayClip(StateNames[(int)CharacterState.Airborne]);
            return;
        }

        // 상태가 그대로여도 클립을 다시 틀어야 하는 경우가 둘 있다.
        // 새로 맞은 순간(피격 클립을 처음부터)과, 콤보 다음 단계로 넘어간 순간(다른 공격 클립으로).
        // animator.Play는 이미 재생 중인 상태도 0프레임으로 되감아 준다.
        bool replay = next != state
                      || (hitRetriggered && next == CharacterState.Hit)
                      || (next == CharacterState.Attack && attack.AttackStartCount != playedAttackStartCount);
        hitRetriggered = false;

        if (!replay) return;

        state = next;
        playedAttackStartCount = attack.AttackStartCount;

        // 공격은 어느 스킬이냐에 따라 클립이 달라지므로 이름을 스킬에서 가져온다.
        string stateName = state == CharacterState.Attack && attack.CurrentSkill != null
            ? attack.CurrentSkill.animatorStateName
            : StateNames[(int)state];

        // 위 replay가 선 프레임에만 부른다. 매 프레임 부르면 클립이 0프레임에 붙박이가 된다.
        PlayClip(stateName);
    }

    /// <summary>애니메이터 상태를 0프레임부터 튼다. 애니메이터가 없으면 아무것도 하지 않는다.</summary>
    void PlayClip(string stateName)
    {
        if (animator != null)
            animator.Play(stateName, 0, 0f);
    }

    /// <summary>
    /// 해당 애니메이터 상태의 클립이 끝까지 돌았는지. 공중 피격·착지·일어서기처럼 한 번만 트는 클립에 쓴다.
    /// Play 직후 한 프레임은 애니메이터가 아직 이전 상태를 돌려주지만, 그땐 상태가 달라서 false가 나오므로 괜찮다.
    /// 컨트롤러에 그 상태가 없으면(예: 플레이어에 착지 클립이 없음) 끝난 것으로 본다. 안 그러면 거기서 영영 못 빠져나온다.
    /// </summary>
    bool ClipFinished(int stateHash)
    {
        if (animator == null || !animator.HasState(0, stateHash)) return true;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash == stateHash && info.normalizedTime >= 1f;
    }

    CharacterState Evaluate()
    {
        // 띄워진 상태는 경직과 무관하게 착지할 때까지 간다. 착지했으면 여기서 끄고 쓰러짐을 시작한다.
        if (airborneHit && movement.IsGrounded)
        {
            airborneHit = false;
            downPhase = CharacterState.AirborneLanded;
        }
        if (airborneHit) return CharacterState.Airborne;

        if (downPhase != CharacterState.Idle)
        {
            AdvanceDownPhase();
            if (downPhase != CharacterState.Idle) return downPhase;
        }

        if (hitTimer > 0f) return CharacterState.Hit;
        if (attack.IsAttacking) return CharacterState.Attack;

        // 준비 클립의 마지막 프레임 이벤트가 이륙시키면, 다음 판정에서 공중이 되어 체공 클립으로 넘어간다.
        if (movement.IsJumpReady) return CharacterState.JumpReady;

        // 점프가 아니라 넉백으로 떠 있어도 여기로 온다. 그래서 준비 동작은 따로 떼어 두었다.
        if (!movement.IsGrounded) return CharacterState.JumpAir;

        // 준비 중엔 제자리에 서 있어도 속도는 입력대로 남아 있으므로, 위의 준비 판정이 먼저 와야 한다.
        if (movement.HorizontalSpeed > MoveSpeedThreshold)
            return movement.IsRunning ? CharacterState.Run : CharacterState.Move;

        return CharacterState.Idle;
    }

    /// <summary>
    /// 쓰러짐 단계를 넘긴다. 착지 → 누움 → 일어서기 → 끝(Idle).
    /// 클립 끝은 그 단계의 클립이 실제로 틀어진 뒤(state가 따라온 뒤)에만 본다.
    /// </summary>
    void AdvanceDownPhase()
    {
        switch (downPhase)
        {
            case CharacterState.AirborneLanded:
                if (state == CharacterState.AirborneLanded && ClipFinished(AirborneLandedStateHash))
                {
                    downPhase = CharacterState.LieDown;
                    lieDownTimer = lieDownDuration;
                }
                break;

            case CharacterState.LieDown:
                lieDownTimer -= Time.deltaTime;
                if (lieDownTimer <= 0f)
                    downPhase = CharacterState.GetUp;
                break;

            case CharacterState.GetUp:
                if (state == CharacterState.GetUp && ClipFinished(GetUpStateHash))
                    downPhase = CharacterState.Idle;
                break;
        }
    }
}