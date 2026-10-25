using UnityEngine;

/// <summary>
/// EnemyBrain의 판단 과정을 눈으로 보여주는 디버그 전용 컴포넌트. 게임 동작에는 아무 영향이 없다.
///
/// 두 가지를 그린다:
///  1. 기즈모 — 이 적을 선택했을 때만 Scene 뷰(와 Gizmos 버튼을 켠 Game 뷰)에 그린다.
///     감지 범위(노랑) · 교전 범위(주황) · 대기 거리(파랑) · 밀어내기 범위 절반(보라) 원, 공격 사거리(빨강) 사각형,
///     목표 지점과 이동선, Approach 중의 Engage 진입 범위(초록), 달리기 경계선(하늘색).
///  2. 상태 글자 — Game 뷰의 적 머리 위에 항상 띄운다. 정식 빌드에는 들어가지 않는다
///     (에디터와 Development Build에서만 컴파일된다).
///
/// 원은 바닥 평면(X·Z)에 납작하게 그린다. 거리 판정이 전부 높이를 빼고 하기 때문이다.
///
/// [준비물] 같은 오브젝트에 EnemyBrain. 상태 글자는 씬의 MainCamera 태그 카메라 기준으로 띄운다.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class EnemyBrainGizmo : MonoBehaviour
{
    /// <summary>원을 몇 개의 선분으로 그릴지. 클수록 매끄럽다.</summary>
    const int CircleSegments = 48;

    static readonly Color DetectColor = Color.yellow;
    static readonly Color EngageColor = new Color(1f, 0.5f, 0f);
    static readonly Color StandoffColor = new Color(0.3f, 0.5f, 1f);
    static readonly Color AttackColor = Color.red;
    static readonly Color GoalColor = Color.green;
    static readonly Color RunLineColor = Color.cyan;
    static readonly Color SeparationColor = new Color(0.7f, 0.3f, 1f);

    [Header("기즈모 (선택했을 때만)")]
    [Tooltip("감지 범위(노랑) · 교전 범위(주황) · 대기 거리(파랑) · 밀어내기 범위 절반(보라) 원과 공격 사거리(빨강) 사각형. " +
             "공격 사거리는 실제 판정처럼 좌우 양쪽에 그린다.")]
    [SerializeField] private bool showRanges = true;

    [Tooltip("지금 향하는 목표 지점(배회 방향·대기 위치·공격 위치)과 거기까지의 선(초록). Hold·Approach·Engage·Rush 중에만. " +
             "Approach 중에는 대기 위치 주변의 Engage 진입 범위 원을, Hold 중에는 배회 경계 세로선 두 줄(회색)을 함께 그린다 — " +
             "동작을 마쳤을 때 안쪽 선보다 플레이어에 가까우면 물러나고, 바깥쪽 선보다 멀면 다가간다.")]
    [SerializeField] private bool showGoal = true;

    [Tooltip("목표 지점 좌우로 runDistance만큼 떨어진 세로선(하늘색). 이 선 바깥이면 달린다. Hold·Approach·Engage·Rush 중에만.")]
    [SerializeField] private bool showRunLines = true;

    [Header("상태 글자 (Game 뷰, 항상)")]
    [Tooltip("적 머리 위에 지금 상태와 타이머를 띄운다.")]
    [SerializeField] private bool showStateLabel = true;

    [Tooltip("글자를 띄울 높이. 몸 위치에서 위로 몇 m.")]
    [SerializeField] private float labelHeight = 2f;

    [Tooltip("글자 크기.")]
    [SerializeField] private int labelFontSize = 16;

    EnemyBrain brain;

    /// <summary>상태 글자를 띄울 기준 카메라. OnGUI는 프레임마다 여러 번 불리므로 Camera.main을 Awake에서 한 번만 찾는다.</summary>
    Camera cam;

    /// <summary>글자 스타일. GUI.skin은 OnGUI 안에서만 읽을 수 있어서 처음 그릴 때 만든다.</summary>
    GUIStyle labelStyle;

    void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        cam = Camera.main;
    }

    void OnDrawGizmosSelected()
    {
        // Play 전에는 Awake가 돌지 않았으므로 여기서 찾는다. 에디터 전용 경로라 매번 찾아도 괜찮다.
        if (brain == null)
            brain = GetComponent<EnemyBrain>();

        Vector3 center = brain.BodyPosition;

        if (showRanges)
        {
            DrawCircle(center, brain.DetectRange, DetectColor);

            // 밀어내기 범위의 절반. 두 적의 원이 겹치기 시작하는 순간이 이동 중 서로 밀어내기 시작하는 순간이다.
            DrawCircle(center, brain.SeparationRadius * 0.5f, SeparationColor);

            // 아래 값들은 스킬 사거리에서 나온다. 스킬이 비어 있으면 그릴 수 없다.
            if (brain.HasSkill)
            {
                DrawCircle(center, brain.EngageRange, EngageColor);
                DrawCircle(center, brain.StandoffDistance, StandoffColor);
                DrawAttackRange(center);
            }
        }

        if (!brain.HasGoal) return;

        Vector3 goal = brain.Goal;

        if (showGoal)
        {
            DrawCircle(goal, EnemyBrain.ArriveRadius, GoalColor);
            Gizmos.DrawLine(center, goal);

            // Approach 중에만 의미가 있다. 이 원 안에 들어오면 Engage로 넘어간다.
            if (brain.State == EnemyBrain.AIState.Approach && !brain.IsStunned)
                DrawCircle(goal, brain.EngageEnterRadius, GoalColor);

            // Hold 중에는 내 쪽의 배회 경계를 그린다. 거리는 X만 보므로 세로선이다.
            if (brain.State == EnemyBrain.AIState.Hold && !brain.IsStunned && brain.HasSkill)
                DrawWanderLines(center);
        }

        if (showRunLines)
        {
            // 달리기 여부는 X 거리만 보므로, Z 방향으로 길게 그은 세로선 두 개로 나타낸다.
            // 맵이 있으면 맵의 앞뒤 끝까지, 없으면 목표 지점 앞뒤로 runDistance만큼 긋는다.
            MapBounds map = MapBounds.Instance;
            float run = brain.RunDistance;
            float zMin = map != null ? map.Bounds.min.z : goal.z - run;
            float zMax = map != null ? map.Bounds.max.z : goal.z + run;

            Gizmos.color = RunLineColor;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = goal.x + side * run;
                Gizmos.DrawLine(new Vector3(x, goal.y, zMin), new Vector3(x, goal.y, zMax));
            }
        }
    }

    /// <summary>
    /// 플레이어 기준 내 쪽(좌/우)에 배회 경계 두 줄을 긋는다. 안쪽이 WanderMinDistance, 바깥쪽이 WanderMaxDistance.
    /// 맵이 있으면 맵의 앞뒤 끝까지, 없으면 몸 앞뒤로 바깥쪽 거리만큼 긋는다.
    /// </summary>
    void DrawWanderLines(Vector3 center)
    {
        Vector3 t = brain.TargetPosition;
        float side = center.x >= t.x ? 1f : -1f;

        MapBounds map = MapBounds.Instance;
        float zMin = map != null ? map.Bounds.min.z : center.z - brain.WanderMaxDistance;
        float zMax = map != null ? map.Bounds.max.z : center.z + brain.WanderMaxDistance;

        Gizmos.color = Color.gray;
        float xMin = t.x + side * brain.WanderMinDistance;
        float xMax = t.x + side * brain.WanderMaxDistance;
        Gizmos.DrawLine(new Vector3(xMin, center.y, zMin), new Vector3(xMin, center.y, zMax));
        Gizmos.DrawLine(new Vector3(xMax, center.y, zMin), new Vector3(xMax, center.y, zMax));
    }

    /// <summary>
    /// 공격 사거리를 좌우 양쪽에 사각형으로 그린다. EnemyBrain.InRange와 같은 기준이다 —
    /// X는 판정 상자 안쪽 끝~바깥쪽 끝(Lunge 포함), Z는 상자 폭의 절반.
    /// 상대의 중심이 이 안에 들어와야 친다.
    /// </summary>
    void DrawAttackRange(Vector3 center)
    {
        float near = Mathf.Max(brain.MinReach, 0f);
        float far = brain.Reach;
        float z = brain.ZTolerance;

        Gizmos.color = AttackColor;
        for (int side = -1; side <= 1; side += 2)
        {
            float x0 = center.x + side * near;
            float x1 = center.x + side * far;
            Vector3 a = new Vector3(x0, center.y, center.z - z);
            Vector3 b = new Vector3(x1, center.y, center.z - z);
            Vector3 c = new Vector3(x1, center.y, center.z + z);
            Vector3 d = new Vector3(x0, center.y, center.z + z);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }

    /// <summary>바닥 평면(X·Z)에 납작한 원을 그린다. Gizmos.DrawWireSphere는 구라서 위에서 보면 겹쳐 보인다.</summary>
    static void DrawCircle(Vector3 center, float radius, Color color)
    {
        Gizmos.color = color;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / CircleSegments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 디버그 표시라 문자열 할당은 감수한다. 정식 빌드에는 이 함수가 아예 없다.
    void OnGUI()
    {
        if (!showStateLabel || cam == null) return;

        Vector3 screen = cam.WorldToScreenPoint(brain.BodyPosition + Vector3.up * labelHeight);
        if (screen.z <= 0f) return;   // 카메라 뒤쪽

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
        }
        labelStyle.fontSize = labelFontSize;

        // 화면 좌표는 아래가 0, GUI 좌표는 위가 0이라 Y를 뒤집는다.
        Rect rect = new Rect(screen.x - 150f, Screen.height - screen.y - 15f, 300f, 30f);
        string text = LabelText();

        // 배경과 상관없이 읽히도록 검은 그림자를 먼저 한 픽셀 비껴 깐다.
        labelStyle.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, labelStyle);

        labelStyle.normal.textColor = StateColor(brain.State);
        GUI.Label(rect, text, labelStyle);
    }

    /// <summary>상태 이름과, 그 상태에서 돌고 있는 타이머.</summary>
    string LabelText()
    {
        string text;
        switch (brain.State)
        {
            case EnemyBrain.AIState.Engage:
                text = $"Engage {brain.EngageTimer:0.00} / {brain.EngageDuration:0.00}s";
                // 플레이어가 휘두르는 중이라 시간을 멈추고 빈틈을 기다리는 중.
                if (brain.IsWaitingForOpening)
                    text += " (대기)";
                break;
            case EnemyBrain.AIState.Hold:
                // 배회 중 지금 동작. 따라가기는 시간이 아니라 도착으로 끝나서 타이머를 띄우지 않는다.
                if (brain.IsWanderPaused)
                    text = $"Hold 멈춤 {brain.WanderTimer:0.0}s";
                else if (brain.IsWanderCatchingUp)
                    text = "Hold 따라감";
                else
                    text = $"Hold 이동 {DirectionArrow(brain.WanderDirection)} {brain.WanderTimer:0.0}s";
                break;
            case EnemyBrain.AIState.Receive:
                text = $"Receive {brain.StateTimer:0.00}s";
                break;
            case EnemyBrain.AIState.Rush:
                text = $"Rush {brain.StateTimer:0.00}s";
                break;
            case EnemyBrain.AIState.Attack:
                // 지금 콤보 단계(스킬 에셋 이름)와, 그 단계가 맞았는지. 맞았으면 다음 단계로 이어진다.
                SkillData skill = brain.CurrentAttackSkill;
                text = skill != null ? $"Attack {skill.name}" : "Attack";
                if (brain.AttackHitLanded)
                    text += " (명중)";
                break;
            case EnemyBrain.AIState.Recover:
                text = $"Recover {brain.StateTimer:0.00}s";
                break;
            default:
                text = brain.State.ToString();
                break;
        }

        // 맞는 중엔 State가 '풀린 뒤 돌아갈 상태(Approach 또는 Hold)'를 가리키므로 따로 표시한다.
        if (brain.IsStunned)
            text += " (피격)";

        // 쥐고 있는 공격권. 플레이어 왼쪽이면 [L], 오른쪽이면 [R].
        if (brain.TokenSide < 0)
            text = "[L] " + text;
        else if (brain.TokenSide > 0)
            text = "[R] " + text;

        return text;
    }

    /// <summary>배회 방향을 화살표로. 위(↑)가 월드 +Z(화면 안쪽)다. 갈 곳이 없어 zero면 빈칸.</summary>
    static string DirectionArrow(Vector2 dir)
    {
        if (dir.x > 0f) return "→";
        if (dir.x < 0f) return "←";
        if (dir.y > 0f) return "↑";
        if (dir.y < 0f) return "↓";
        return "";
    }

    static Color StateColor(EnemyBrain.AIState s)
    {
        switch (s)
        {
            case EnemyBrain.AIState.Hold:     return Color.gray;
            case EnemyBrain.AIState.Receive:  return new Color(1f, 0.6f, 0.8f);
            case EnemyBrain.AIState.Approach: return GoalColor;
            case EnemyBrain.AIState.Engage:   return EngageColor;
            case EnemyBrain.AIState.Rush:     return new Color(1f, 0.3f, 0.3f);
            case EnemyBrain.AIState.Attack:   return AttackColor;
            case EnemyBrain.AIState.Recover:  return RunLineColor;
            default:                          return Color.white;   // Idle
        }
    }
#endif
}
