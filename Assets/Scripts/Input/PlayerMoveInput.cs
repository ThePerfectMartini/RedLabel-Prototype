using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 이동·점프·공격 입력을 읽어 MovementCore / AttackCore에 넘겨주는 컴포넌트.
///
/// .inputactions 에셋을 쓰지 않고 코드에서 직접 바인딩한다 —
/// 키 배치가 이 파일 하나에 전부 보이는 게 이 규모에선 더 단순하다.
///
/// [조작] WASD · 방향키 · 패드 왼쪽 스틱 이동 / C · 패드 A 점프
///        X · 패드 서쪽 버튼 기본공격 / Z · 패드 북쪽 버튼 특수공격
///        A/D · ←/→ 를 빠르게 두 번 누르고 유지하면 달리기
///        달리는 중 X · 패드 서쪽 버튼 대시공격
///        공중에서 X · 패드 서쪽 버튼 점프공격
///
/// 어떤 공격이 나가는지는 여기서 정하지 않는다. CharacterSettings에 등록된 스킬을 꺼내 넘길 뿐이다.
/// 콤보도 모른다 — 공격 동작 중에 눌린 키까지 그대로 넘겨주고, 이어갈지 말지는 AttackCore가 정한다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 MovementCore + AttackCore + StateMachine.
/// </summary>
public class PlayerMoveInput : MonoBehaviour
{
    MovementCore movement;
    AttackCore attack;
    StateMachine state;

    /// <summary>어떤 스킬이 어느 키에 걸려 있는지 여기서 읽는다.</summary>
    CharacterSettings settings;

    InputAction moveAction;
    InputAction jumpAction;
    InputAction attack1Action;
    InputAction attack2Action;

    /// <summary>
    /// 달리기 연타 감지 전용 좌우 입력. moveAction을 쓰지 않는 이유: 거기엔 패드 스틱이 묶여 있어
    /// 스틱을 두 번 튕겨도 달리게 된다. 연타는 키보드 좌우 키로만 받는다.
    /// </summary>
    InputAction runTapAction;

    [Tooltip("같은 방향 키를 두 번 누르는 사이의 최대 간격(초). 이 안에 다시 누르고 유지하면 달린다.")]
    [SerializeField] float doubleTapWindow = 0.25f;

    /// <summary>직전 프레임에 눌려 있던 좌우 방향(-1, 0, 1). 새로 눌린 순간을 잡는 데 쓴다.</summary>
    int heldDir;

    /// <summary>마지막으로 새로 눌린 좌우 방향과 그 시각. 다음 누름이 연타인지 가린다.</summary>
    int lastTapDir;
    float lastTapTime = float.NegativeInfinity;

    bool running;

    /// <summary>
    /// X키 콤보를 시작한 스킬(기본공격 또는 대시공격). 공격이 새로 시작될 때만 고르고, 동작 중에는
    /// 그대로 넘겨 AttackCore가 같은 콤보의 입력으로 알아보게 한다. 매번 새로 고르면 대시공격 중의
    /// X 입력이 기본공격으로 넘어가 콤보 입력으로 인정되지 않는다.
    /// </summary>
    SkillData attack1Skill;

    void Awake()
    {
        // "Move"라는 추상적인 행동을 만들고, 거기에 장치별 바인딩을 얹는다.
        // Value 타입 = 매 프레임 현재 값을 읽는 용도(누르고 있는 동안 계속).
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

        // 키 4개를 묶어 하나의 Vector2로 합성한다. 대각선은 자동으로 정규화된다.
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        // 같은 행동에 바인딩을 더 얹을 수 있다. 방향키로도 똑같이 움직인다.
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        // 게임패드 지원은 이 한 줄이 전부다. 스틱은 이미 Vector2라 합성이 필요 없다.
        moveAction.AddBinding("<Gamepad>/leftStick");

        // Button 타입 = 눌린 '순간'을 잡는 용도.
        jumpAction = new InputAction("Jump", InputActionType.Button, binding: "<Keyboard>/c");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        attack1Action = new InputAction("Attack1", InputActionType.Button, binding: "<Keyboard>/x");
        attack1Action.AddBinding("<Gamepad>/buttonWest");

        attack2Action = new InputAction("Attack2", InputActionType.Button, binding: "<Keyboard>/z");
        attack2Action.AddBinding("<Gamepad>/buttonNorth");

        // 1DAxis = 음/양 키 두 개를 -1~1 값 하나로 합성한다.
        runTapAction = new InputAction("RunTap", InputActionType.Value, expectedControlType: "Axis");
        runTapAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d");
        runTapAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/rightArrow");

        // Core들은 LogicCores 같은 자식으로 내려가 있을 수 있다.
        // RequireComponent를 쓰지 않는 이유: 그걸 달면 유니티가 이 오브젝트에 Core를 다시 붙여버려서,
        // 자식으로 내려놓은 Core와 같은 컴포넌트가 둘씩 돌게 된다.
        movement = GetComponentInChildren<MovementCore>();
        attack = GetComponentInChildren<AttackCore>();
        state = GetComponentInChildren<StateMachine>();

        Character character = GetComponentInParent<Character>();
        if (character != null)
            settings = character.Settings;

        if (movement == null || attack == null || state == null || settings == null)
        {
            Debug.LogError($"{name}: MovementCore / AttackCore / StateMachine / 캐릭터 셋팅 중 일부를 찾지 못했습니다. 입력을 끕니다.", this);
            enabled = false;
        }
    }

    // 활성화된 InputAction만 값을 읽을 수 있다. 오브젝트가 꺼지면 같이 꺼져야 한다.
    void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        attack1Action.Enable();
        attack2Action.Enable();
        runTapAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        attack1Action.Disable();
        attack2Action.Disable();
        runTapAction.Disable();
    }

    // 코드로 만든 InputAction은 에셋에 속하지 않아 유니티가 정리해주지 않는다. 직접 해제한다.
    void OnDestroy()
    {
        moveAction?.Dispose();
        jumpAction?.Dispose();
        attack1Action?.Dispose();
        attack2Action?.Dispose();
        runTapAction?.Dispose();
    }

    void Update()
    {
        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        UpdateRun(moveInput);

        switch (state.State)
        {
            case CharacterState.Hit:
            case CharacterState.Airborne:
            case CharacterState.AirborneLanded:
            case CharacterState.LieDown:
            case CharacterState.GetUp:
                // 아무것도 하지 않는다. SetMoveInput을 부르면 넉백 속도가 지워진다.
                // 달리기는 끊는다 — 경직이 풀린 뒤엔 키를 누르고 있어도 걷기부터 다시 시작한다.
                running = false;
                break;

            case CharacterState.Attack:
                // 지상에선 아무것도 하지 않는다. SetMoveInput을 부르면 Lunge Attack의 이동 속도가 지워진다.
                // 멈추는 건 공격 시작 때 AttackCore가 스킬의 공격 타입으로 정한다.
                // 입력을 넘기지 않으므로 공격 중에는 바라보는 방향도 바뀌지 않는다.
                // 공중에선 넘긴다 — 점프공격 중에도 좌우·앞뒤 조작이 되게. 공중에선 Lunge가 없고 방향도 안 바뀐다.
                if (!movement.IsGrounded)
                    movement.SetMoveInput(moveInput);
                running = false;
                break;

            default:
                movement.SetMoveInput(moveInput, running);
                break;
        }

        // isPressed가 아니라 WasPressedThisFrame — 누르고 있는 동안 계속 나가면 안 되므로.
        // 여기선 준비 동작만 시작한다. 실제로 뜨는 건 준비 클립 끝의 Animation Event다.
        if (jumpAction.WasPressedThisFrame() && state.CanAct)
            movement.RequestJump();

        // 공격키만 CanAct가 아니라 경직 여부로 판단한다. 공격 동작 중의 입력은 막을 게 아니라
        // 콤보 선입력으로 받아야 하기 때문이다. 이어갈 단계가 있는지는 AttackCore가 가린다.
        // 점프 준비 중엔 막는다 — 한번 웅크렸으면 뜨는 데까지 간다. state가 아니라 movement를 직접
        // 보는 이유: 상태는 한 프레임 늦어서, 점프와 같은 프레임에 누른 공격이 새어 들어간다.
        // 맞아서 떠 있는 동안(Airborne)도 막는다 — 떨어지는 도중 공중 공격으로 빠져나가지 못하게.
        // 착지 후 쓰러져 있는 동안도 일어설 때까지 막는다.
        bool attackInputAllowed = state.State != CharacterState.Hit
                                  && state.State != CharacterState.Airborne
                                  && !state.IsDown
                                  && !movement.IsJumpReady;

        if (attack1Action.WasPressedThisFrame() && attackInputAllowed)
        {
            // state.State가 아니라 IsAttacking으로 본다. 상태는 한 프레임 늦게 바뀔 수 있어서,
            // 공격이 막 끝난 프레임에 이전 스킬이 다시 나갈 수 있다.
            // 공중 여부도 같은 이유로 state(JumpAir)가 아니라 movement를 직접 본다.
            if (!attack.IsAttacking)
            {
                if (!movement.IsGrounded && settings.jumpAttack != null)
                    attack1Skill = settings.jumpAttack;
                else if (state.State == CharacterState.Run && settings.dashAttack != null)
                    attack1Skill = settings.dashAttack;
                else
                    attack1Skill = settings.basicAttack;
            }

            attack.TryAttack(attack1Skill);
        }

        if (attack2Action.WasPressedThisFrame() && attackInputAllowed)
            attack.TryAttack(settings.specialAttack);
    }

    /// <summary>
    /// 좌우 키가 새로 눌린 순간만 본다. 직전에 누른 방향과 같고 doubleTapWindow 안이면 달리기를 켠다.
    ///
    /// 한번 켜진 달리기는 좌우 키를 떼도 W/S로 계속 움직이는 한 유지된다.
    /// 끝나는 건 이동키를 전부 뗐을 때와, 반대 방향 키를 눌렀을 때 둘뿐이다.
    /// </summary>
    void UpdateRun(Vector2 moveInput)
    {
        if (moveInput.sqrMagnitude <= 0.0001f)
            running = false;

        float x = runTapAction.ReadValue<float>();
        int dir = x > 0.5f ? 1 : x < -0.5f ? -1 : 0;

        if (dir == heldDir) return;
        heldDir = dir;

        // 좌우 키만 뗀 것이다. 달리기 여부는 위의 '이동키 전부 뗌'이 가린다.
        if (dir == 0) return;

        // 달리던 방향을 다시 누른 것 — 연타 판정 없이 그대로 달린다.
        // 달리는 동안 lastTapDir는 늘 달리는 방향이다(다른 방향이 눌렸다면 이미 갱신됐으므로).
        if (running && dir == lastTapDir) return;

        // 여기부터는 새 누름. 반대 방향이면 lastTapDir와 달라 false가 되어 달리기가 끝난다.
        running = dir == lastTapDir && Time.time - lastTapTime <= doubleTapWindow;
        lastTapDir = dir;
        lastTapTime = Time.time;
    }
}