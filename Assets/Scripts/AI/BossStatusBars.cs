using UnityEngine;

/// <summary>
/// 보스 머리 위에 체력 바(빨강)와 그로기 게이지 바(노랑)를 그리는 임시 표시. 진짜 UI가 생기면 이 컴포넌트를 지운다.
///
/// 씬에 UI를 만들지 않으려고 OnGUI로 화면에 사각형을 직접 그린다. 값은 읽기만 한다.
/// 보스가 죽어 오브젝트가 사라지면 바도 같이 사라진다.
///
/// [준비물] 같은 오브젝트에 BossBrain, 계층 위쪽에 Character, 그 아래 어딘가에 Health. 씬에 MainCamera 태그가 붙은 카메라.
/// </summary>
public class BossStatusBars : MonoBehaviour
{
    [Tooltip("몸 위치에서 바까지의 높이(m).")]
    [SerializeField] private float heightOffset = 2.5f;

    [Tooltip("바 하나의 크기(픽셀). X는 너비, Y는 높이.")]
    [SerializeField] private Vector2 barSize = new Vector2(120f, 10f);

    [Tooltip("체력 바와 그로기 바 사이 간격(픽셀).")]
    [SerializeField] private float barSpacing = 4f;

    BossBrain brain;
    Health health;
    Transform body;
    Camera cam;

    void Awake()
    {
        brain = GetComponent<BossBrain>();

        Character character = GetComponentInParent<Character>();
        if (character != null)
        {
            body = character.transform;
            health = character.GetComponentInChildren<Health>();
        }

        // Camera.main은 매번 찾으므로 OnGUI에서 부르지 않고 여기서 한 번만 찾아둔다.
        cam = Camera.main;

        if (brain == null || body == null || health == null || cam == null)
        {
            Debug.LogError($"{name}: BossBrain / Character / Health / 메인 카메라 중 일부를 찾지 못해 바를 그리지 않습니다.", this);
            enabled = false;
        }
    }

    void OnGUI()
    {
        // OnGUI는 한 프레임에 여러 번 불린다. 그리는 건 Repaint 때 한 번이면 된다.
        if (Event.current.type != EventType.Repaint) return;

        Vector3 screen = cam.WorldToScreenPoint(body.position + Vector3.up * heightOffset);
        if (screen.z <= 0f) return;   // 카메라 뒤

        // 화면 좌표는 아래가 0이고 GUI 좌표는 위가 0이라 뒤집는다.
        float x = screen.x - barSize.x * 0.5f;
        float y = Screen.height - screen.y;

        DrawBar(new Rect(x, y, barSize.x, barSize.y), health.Current / health.Max, Color.red);
        DrawBar(new Rect(x, y + barSize.y + barSpacing, barSize.x, barSize.y),
                brain.GroggyGauge / brain.GroggyGaugeMax, Color.yellow);
    }

    /// <summary>검은 배경 위에 ratio(0~1)만큼 채운 바를 그린다.</summary>
    static void DrawBar(Rect rect, float ratio, Color fill)
    {
        GUI.color = Color.black;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        rect.width *= Mathf.Clamp01(ratio);
        GUI.color = fill;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        GUI.color = Color.white;
    }
}
