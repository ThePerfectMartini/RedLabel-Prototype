using UnityEngine;

/// <summary>
/// 캐릭터의 이동을 담당하는 컴포넌트. X·Z 평면 이동과 Y축 점프·낙하를 직접 계산해
/// Character의 transform에 반영한다. Rigidbody를 쓰지 않는다.
///
/// 자기 transform이 아니라 Character의 transform을 움직인다 — 이 스크립트가 LogicCores 같은
/// 자식으로 내려가 있어도, 실제로 움직여야 하는 건 캐릭터의 몸통이기 때문이다.
///
/// 키 입력은 전혀 모른다 — 외부가 SetMoveInput / RequestJump / Launch / Lunge로 "뭘 하고 싶은지"만 넣어준다.
/// 수치는 직접 갖지 않고 Character가 들고 있는 CharacterSettings 에셋에서 읽는다.
///
/// [준비물] 계층 위쪽에 Character. 그 아래 어딘가에 Collider 하나(몸집·발바닥 계산용).
///          씬에 MapBounds 하나(없으면 경계 없음).
/// </summary>
public class MovementCore : MonoBehaviour
{
    /// <summary>
    /// 모든 캐릭터가 공유하는 기본 낙하 가속도. 캐릭터별 차이는 오직 무게 배율로만 준다.
    /// 캐릭터마다 다를 수 있는 값이 아니라서 CharacterSettings에 넣지 않았다 —
    /// 에셋마다 다른 중력을 적을 수 있게 되면 세계가 일관성을 잃는다.
    /// 나중에 전역 물리 에셋을 만들면 이 상수가 그쪽으로 옮겨간다.
    /// </summary>
    const float BaseGravity = 40f;

    /// <summary>시작할 때 바닥을 찾으려고 발밑으로 쏘는 레이의 최대 길이(m).</summary>
    const float GroundProbeDistance = 100f;

    /// <summary>
    /// 떨어진 높이 대비 튕겨 오르는 높이 비율의 상한. 1 이상이면 매번 같거나 더 높이 튀어 영영 멈추지 않는다.
    /// </summary>
    const float MaxBounceRatio = 0.9f;

    /// <summary>이 Core가 속한 캐릭터. 셋팅과 몸 transform을 여기서 받아간다.</summary>
    Character character;

    /// <summary>실제로 움직이는 transform. Character의 것이다.</summary>
    Transform body;

    /// <summary>Character에서 받아온 설정 에셋. 비어 있으면 움직이지 않는다.</summary>
    CharacterSettings settings;

    Vector3 velocity;

    /// <summary>
    /// 이번 프레임에 SetMoveInput이 들어왔는지. 들어온 프레임엔 지상 마찰을 건너뛴다.
    /// 입력은 매 프레임 들어오므로, 입력 쪽과 이 스크립트의 Update 순서와 무관하게 동작한다.
    /// </summary>
    bool driven;

    /// <summary>
    /// 이번 공중 체류가 '위로 띄워진' 것인지. 그렇다면 착지하는 순간 수평 속도를 끊는다.
    /// 에어본이 끝났는데도 날아오던 관성으로 계속 미끄러져 보이는 걸 막기 위해서다.
    /// 위로 뜨지 않는 지상 넉백은 이 값이 false라, 마찰이 서서히 죽이도록 남겨둔다.
    /// </summary>
    bool stopOnLanding;

    /// <summary>
    /// 점프 준비 동작 중인지. RequestJump가 켜고, 이륙(Jump)하거나 Launch로 밀려나면 꺼진다.
    /// 켜져 있는 동안은 땅에 선 채로 수평 위치를 움직이지 않는다.
    /// </summary>
    bool jumpReady;

    /// <summary>
    /// 공중에서 이동 입력을 받는지. Jump로 떠올랐을 때만 켜지고, 착지하거나 Launch로 밀려나면 꺼진다 —
    /// 맞아서 뜬 경우엔 경직이 공중에서 풀려도 넉백 궤적을 끊지 못하게 하려는 것이다.
    /// </summary>
    bool airControl;

    /// <summary>
    /// 이번 공중 체류에서 가장 높이 올라간 피벗 높이. 착지할 때 '떨어진 높이'를 재서 튕길지 정하는 데 쓴다.
    /// Launch가 뜨는 순간의 높이로 채우고, 공중에 있는 동안 계속 갱신한다. 튕기면 바닥 높이로 다시 채운다.
    /// </summary>
    float peakY;

    /// <summary>
    /// 바닥에 서 있을 때의 피벗 높이. Awake에서 발밑으로 레이를 한 번 쏴 정하고 그대로 고정된다.
    /// 평평한 바닥 하나를 가정하는 방식이라, 높이가 다른 발판이 생기면 매 프레임 재측정으로 바꿔야 한다.
    /// </summary>
    float groundY;

    bool isGrounded = true;
    /// <summary>
    /// 바라보는 좌/우. 벨트스크롤 방식이라 앞뒤(Z)로 걸어도 방향은 좌·우 둘 중 하나만 유지한다.
    /// 공중에서는 바뀌지 않는다(SetMoveInput이 공중에선 방향을 갱신하기 전에 빠져나가므로 자연히 그렇게 된다).
    /// </summary>
    bool isFacingRight = true;

    /// <summary>지상에서 마지막으로 받은 입력이 달리기였는지. 공중에서는 뜨기 직전 값을 유지한다.</summary>
    bool isRunning;

    /// <summary>
    /// 몸집 반지름. 경계에서 '중심점'이 아니라 '몸의 옆면'이 멈추게 하는 여유분이다.
    /// Collider 크기에서 Awake 때 한 번 계산한다.
    /// </summary>
    float boundaryRadius;

    /// <summary>피벗에서 발바닥까지의 거리. 바닥면 높이를 '서 있을 때의 피벗 높이'로 바꾸는 데 쓴다.</summary>
    float footOffset;

    /// <summary>씬의 MapBounds. 없으면 null이고 경계 처리를 건너뛴다.</summary>
    MapBounds map;

    /// <summary>땅에 닿아 있는지. 애니메이션이나 다른 컴포넌트가 읽는다.</summary>
    public bool IsGrounded => isGrounded;
    /// <summary>바라보는 방향. 좌우로만 뒤집히므로 Vector3.right 아니면 Vector3.left.</summary>
    public Vector3 FacingDir => isFacingRight ? Vector3.right : Vector3.left;

    /// <summary>수평 이동 속도(초당). 걷기와 서기를 가르는 데 쓴다.</summary>
    public float HorizontalSpeed => new Vector2(velocity.x, velocity.z).magnitude;

    /// <summary>달리기 속도로 움직이는 중인지. 걷기와 달리기를 가르는 데 쓴다.</summary>
    public bool IsRunning => isRunning;

    /// <summary>점프 준비 동작 중인지. StateMachine이 준비 클립을 틀고, 입력 쪽이 공격을 막는 데 쓴다.</summary>
    public bool IsJumpReady => jumpReady;

    void Awake()
    {
        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        character = GetComponentInParent<Character>();
        if (character == null)
        {
            Debug.LogError($"{name}: 계층 위쪽에서 Character를 찾지 못했습니다. 이동을 끕니다.", this);
            enabled = false;
            return;
        }

        body = character.transform;

        // Settings는 직렬화된 필드를 그대로 돌려주므로 Awake 실행 순서와 무관하게 읽어도 된다.
        settings = character.Settings;
        if (settings == null)
        {
            // 에러는 Character 쪽에서 이미 냈다. 여기서는 조용히 멈추기만 한다.
            enabled = false;
            return;
        }

        // Character 아래 전체를 훑는다. 몸 콜라이더가 어느 자식에 있든 상관없게.
        Collider col = character.GetComponentInChildren<Collider>();
        if (col != null)
        {
            // 피벗에서 콜라이더의 가장 먼 면까지의 거리를 잰다.
            // extents를 그냥 쓰지 않는 이유: 자식 콜라이더는 피벗에서 어긋나 있을 수 있어서,
            // 그 경우 '콜라이더 중심 기준 크기'와 '피벗 기준 한계'가 달라진다.
            Bounds b = col.bounds;
            Vector3 p = body.position;
            boundaryRadius = Mathf.Max(
                Mathf.Max(b.max.x - p.x, p.x - b.min.x),
                Mathf.Max(b.max.z - p.z, p.z - b.min.z));
            footOffset = p.y - b.min.y;
        }
        else
        {
            boundaryRadius = 0f;
            footOffset = 0f;
            Debug.LogWarning($"{name}: Character 아래 어디에도 Collider가 없어 몸집을 0으로 둡니다.", this);
        }

        // 발밑으로 레이를 쏴 바닥을 찾는다. 공중에 스폰해도 진짜 바닥을 기준으로 삼기 위해서다.
        // 레이 시작점이 어떤 콜라이더 안에 있으면 유니티는 그 콜라이더를 맞히지 않으므로,
        // 자기 몸은 따로 거르지 않아도 알아서 빠진다. 피벗이 몸 콜라이더 안에 있다는 전제다 —
        // 피벗을 발바닥에 둔 캐릭터라면 자기 콜라이더에 맞아버려서 이 방식이 통하지 않는다.
        if (Physics.Raycast(body.position, Vector3.down, out RaycastHit hit, GroundProbeDistance))
            groundY = hit.point.y + footOffset;
        else
            groundY = body.position.y;   // 못 찾으면 지금 높이를 바닥으로 삼는다(종전 동작)

        // 바닥보다 위에 떠 있으면 공중에서 시작한다. Update의 중력이 알아서 떨어뜨린다.
        // 바닥에 딱 붙여 놓았으면 등호가 성립해 true가 되므로 종전과 동작이 같다.
        isGrounded = body.position.y <= groundY;
        peakY = body.position.y;

        // 매 프레임 찾으면 낭비라 여기서 한 번만 찾아둔다.
        // 값(Bounds)이 아니라 참조만 잡아두므로 MapBounds.Awake가 나중에 돌아도 문제없다.
        map = MapBounds.Instance;
    }

    /// <summary>
    /// 이번 프레임의 이동 의도. x는 좌우(월드 X), y는 앞뒤(월드 Z)에 대응한다.
    /// 공중에서는 점프로 뜬 경우에만 입력이 먹는다(AirControl 참고). 그 외엔 무시되어
    /// 뜨기 직전의 수평 속도가 그대로 유지되며 포물선을 그린다.
    /// 매 프레임 호출해야 한다.
    /// run이 true면 moveSpeed 대신 runSpeed로 움직인다. 멈춰 있으면 달리기로 치지 않는다.
    /// </summary>
    public void SetMoveInput(Vector2 input, bool run = false)
    {
        if (settings == null) return;

        // 입력이 들어왔다는 표시. 이 프레임엔 마찰을 걸지 않는다(Update 1번 참고).
        driven = true;

        if (!isGrounded)
        {
            if (airControl)
                AirControl(input);
            return;
        }

        // 좌우 입력이 있을 때만 방향을 갱신한다. W/S로만 걸으면 보던 쪽을 유지.
        if (Mathf.Abs(input.x) > 0.01f)
            isFacingRight = input.x > 0f;

        // 달리기에 들어간 뒤엔 앞뒤(W/S)로만 가도 달린다. 멈춰 있으면 달리는 게 아니다.
        isRunning = run && input.sqrMagnitude > 0.0001f;
        float speed = isRunning ? settings.runSpeed : settings.moveSpeed;

        Vector3 dir = new Vector3(input.x, 0f, input.y);
        Vector3 horizontal = Vector3.ClampMagnitude(dir, 1f) * speed;

        // y는 건드리지 않는다. 수직 속도는 중력과 Launch만 관리한다.
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;
    }

    /// <summary>
    /// 월드 X 좌표 worldX가 있는 쪽을 바라본다. 속도는 건드리지 않는다 — 방향만 돌린다.
    /// 공중에서는 무시한다(공중에서는 방향이 바뀌지 않는다는 규칙을 따른다).
    /// X가 같으면 어느 쪽인지 정할 수 없으니 보던 쪽을 유지한다.
    /// </summary>
    public void FaceTowards(float worldX)
    {
        if (settings == null) return;
        if (!isGrounded) return;

        float dx = worldX - body.position.x;
        if (Mathf.Abs(dx) > 0.01f)
            isFacingRight = dx > 0f;
    }

    /// <summary>
    /// 움직이지 않고 좌우 방향만 바꾼다. AI가 공격 직전이나 뒷걸음질 중에 상대 쪽을 보게 할 때 쓴다.
    /// dirX의 부호가 곧 방향이고, 0에 가까우면 무시한다.
    /// 공중에서는 무시된다 — SetMoveInput이 공중에서 방향을 바꾸지 않는 것과 같은 규칙이다.
    /// </summary>
    public void Face(float dirX)
    {
        if (!isGrounded) return;

        if (Mathf.Abs(dirX) > 0.01f)
            isFacingRight = dirX > 0f;
    }

    /// <summary>
    /// 점프 중의 좌우·앞뒤 조작. 좌우 속도는 뜨기 직전의 달리기 여부로 정하고(isRunning은 공중에서
    /// 바뀌지 않는다), 앞뒤 속도는 달리기와 무관하게 항상 걷기 속도다.
    /// 키가 하나라도 눌려 있으면 지상처럼 누른 방향으로만 움직인다 — 누르지 않은 축의 속도는 0이 된다.
    /// 키를 전부 떼면 속도를 건드리지 않아 마지막 속도로 관성이 이어진다.
    /// 바라보는 방향은 건드리지 않는다 — 점프를 출발한 그대로 유지된다.
    /// </summary>
    void AirControl(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f) return;

        Vector2 dir = Vector2.ClampMagnitude(input, 1f);
        float speedX = isRunning ? settings.runSpeed : settings.moveSpeed;

        velocity.x = dir.x * speedX;
        velocity.z = dir.y * settings.moveSpeed;
    }

    /// <summary>
    /// 밀거나 띄운다. 지상·공중을 가리지 않는다.
    ///
    /// 수평 속도는 항상 교체한다. 수직은 미는 힘이 있을 때만 교체하고, 0이면 손대지 않는다.
    /// 그래서 순수 넉백으로 맞은 대상은 올라가던 기세든 떨어지던 기세든 그대로 이어간다.
    ///
    /// 바라보는 방향은 건드리지 않는다 — 맞아서 날아가도 보던 쪽을 그대로 본다.
    /// 공중의 대상에게 불러도 먹히므로, 저글링을 허용할지 말지는 부르는 쪽이 정한다
    /// (막고 싶으면 IsGrounded를 먼저 보면 된다).
    /// </summary>
    public void Launch(Vector3 launchVelocity)
    {
        velocity.x = launchVelocity.x;
        velocity.z = launchVelocity.z;

        // 수직으로 미는 힘이 있으면 위로든 아래로든 그대로 덮어쓴다.
        // 0은 '수직 무간섭'이다 — 지금 솟고 있든 떨어지고 있든 그 속도를 유지한다.
        // 공중의 대상을 어떻게 다룰지는 부르는 쪽이 이 값으로 정한다(SkillData의 공중 런치 파워).
        if (launchVelocity.y != 0f)
            velocity.y = launchVelocity.y;

        isGrounded = false;

        // 공중에서 또 맞아도(저글링) 거기서부터 새로 잰다. 그 뒤 더 올라가면 Update가 갱신한다.
        peakY = body.position.y;

        // 준비 동작은 '땅에 서서' 하는 것이다. 이륙이든 피격이든 밀려나는 순간 끝난다.
        jumpReady = false;

        // 맞아서 뜬 경우엔 공중 조작이 없다. 점프는 Jump가 이 뒤에 다시 켠다.
        airControl = false;

        // 위로 미는 힘이 있었을 때만 착지에서 멈춘다. 임계값을 눈대중으로 정하지 않아도
        // '띄웠는가'와 '밀기만 했는가'가 이 부호 하나로 갈린다.
        stopOnLanding = launchVelocity.y > 0f;
    }

    /// <summary>
    /// 점프 준비 동작을 시작한다. 실제로 뜨는 건 준비 클립의 Animation Event가 Jump를 부를 때다.
    /// 공중이거나 이미 준비 중이면 무시된다(2단 점프 없음).
    /// </summary>
    public void RequestJump()
    {
        if (settings == null) return;
        if (!isGrounded || jumpReady) return;

        jumpReady = true;
    }

    /// <summary>
    /// 실제로 위로 뜬다. 준비 클립의 Animation Event가 AnimationEventRelay를 거쳐 호출한다.
    /// 준비 중에도 입력이 속도를 계속 갱신하므로, 이륙 순간 누르고 있던 방향으로 포물선을 그린다.
    /// 준비 중이 아니면 무시된다 — 준비는 지상에서만 켜지므로 이게 곧 지상 제약이다.
    /// </summary>
    public void Jump()
    {
        if (settings == null) return;
        if (!jumpReady) return;

        Launch(new Vector3(velocity.x, settings.jumpPower, velocity.z));
        airControl = true;
    }

    /// <summary>
    /// 바라보는 방향으로 distance(m)만큼 밀려나간다. 공격이 시작될 때 AttackCore가 부른다.
    ///
    /// 수평 속도를 한 번 교체할 뿐이고, 그 뒤로는 지상 마찰이 서서히 죽인다.
    /// 속도가 아니라 거리를 받아 √(2 × groundFriction × 거리)로 바꾸는 이유: 마찰이 다른 캐릭터가
    /// 같은 스킬을 써도 같은 거리만큼 가게 하려고. 0을 주면 그 자리에 멈춘다.
    ///
    /// 공중에서는 무시된다 — 점프 중 공격이면 공중 조작(AirControl)이, 그 외엔 떠오를 때의 속도가 그대로 이어진다.
    /// Launch를 쓰지 않는 이유: 그쪽은 지상에서 불러도 공중 판정으로 바꾸고 착지 처리까지 건드린다.
    /// </summary>
    public void Lunge(float distance)
    {
        if (settings == null) return;
        if (!isGrounded) return;

        float speed = Mathf.Sqrt(2f * settings.groundFriction * distance);
        velocity.x = FacingDir.x * speed;
        velocity.z = 0f;
    }

    /// <summary>
    /// 바라보는 방향으로 speed(m/s)로 움직인다. 등속 돌진(LungeMotion.Constant) 동안 AttackCore가 부른다.
    /// 매 프레임 불러야 한다 — 부른 프레임엔 마찰을 걸지 않는다(SetMoveInput과 같은 처리라 Update 순서와 무관하다).
    /// 공중에서는 무시된다 — Lunge와 같은 규칙이다.
    /// </summary>
    public void MoveForward(float speed)
    {
        if (settings == null) return;
        if (!isGrounded) return;

        driven = true;
        velocity.x = FacingDir.x * speed;
        velocity.z = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // 1) 지상 마찰 — 입력이 들어온 프레임엔 건너뛴다. 입력이 정한 속도에까지 마찰을 먹이면
        //    걷는 속도가 moveSpeed보다 느려지고, 그 손실이 프레임률에 따라 달라진다.
        //    결국 넉백처럼 '입력이 아닌 힘으로 얻은 속도'만 여기서 깎인다.
        if (isGrounded && !driven)
        {
            Vector2 horizontal = new Vector2(velocity.x, velocity.z);
            horizontal = Vector2.MoveTowards(horizontal, Vector2.zero, settings.groundFriction * dt);
            velocity.x = horizontal.x;
            velocity.z = horizontal.y;
        }
        driven = false;

        // 2) 중력 — 공중일 때만. 지상에서도 계속 깎으면 땅 밑으로 파고든다.
        if (!isGrounded)
            velocity.y -= BaseGravity * settings.weight * dt;

        // 3) 위치 적분 — 점프 준비 중엔 제자리에 선다. 속도는 그대로 두어 이륙 때 쓴다.
        //    준비는 지상에서만 켜지고 지상에선 velocity.y가 0이라, 위치를 통째로 멈춰도 된다.
        Vector3 pos = jumpReady ? body.position : body.position + velocity * dt;

        if (!isGrounded && pos.y > peakY)
            peakY = pos.y;

        // 4) 착지 — 3)에서 바닥을 뚫었으면 되돌린다.
        //    맞아서 뜬 경우(공중 조작이 없는 경우)엔 충분히 높이서 떨어졌으면 착지 대신 다시 튕겨 오른다.
        //    점프로 뜬 경우는 튕기지 않는다.
        float fallHeight = peakY - groundY;
        if (pos.y <= groundY && !isGrounded && !airControl && fallHeight >= settings.bounceMinHeight)
        {
            pos.y = groundY;

            // 튕겨 오를 높이를 정하고, 그 높이에 닿는 속도로 바꾼다(v = √(2gh)).
            // 중력에 무게가 곱해지므로 g에도 무게를 곱해야 정한 높이만큼 정확히 뜬다.
            float ratio = Mathf.Min(settings.bounceRatio / settings.weight, MaxBounceRatio);
            float bounceHeight = fallHeight * ratio;
            velocity.y = Mathf.Sqrt(2f * BaseGravity * settings.weight * bounceHeight);

            velocity.x *= settings.bounceHorizontalDamping;
            velocity.z *= settings.bounceHorizontalDamping;

            // 다음 착지 때 이번 튕김의 높이로 다시 재도록 바닥에서부터 센다.
            peakY = groundY;
        }
        else if (pos.y <= groundY)
        {
            pos.y = groundY;
            velocity.y = 0f;

            // 띄워져서 날아온 경우에만 여기서 수평 속도를 끊는다.
            // 그 외(지상 넉백)는 1)의 마찰이 서서히 죽이도록 남겨둔다.
            if (stopOnLanding)
            {
                velocity.x = 0f;
                velocity.z = 0f;
                stopOnLanding = false;
            }

            isGrounded = true;
            airControl = false;
        }

        // 5) 맵 경계 — 3)에서 밖으로 나갔으면 되돌린다.
        if (map != null)
        {
            Bounds b = map.Bounds;
            pos.x = Mathf.Clamp(pos.x, b.min.x + boundaryRadius, b.max.x - boundaryRadius);
            pos.z = Mathf.Clamp(pos.z, b.min.z + boundaryRadius, b.max.z - boundaryRadius);
        }

        body.position = pos;
    }
}