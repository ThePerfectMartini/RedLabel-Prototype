using UnityEditor;

/// <summary>
/// SkillData의 인스펙터. 쓰이지 않는 칸을 회색으로 잠근다.
///  - '띄우는 공격'이 꺼져 있으면 지상 런치 파워 칸.
///  - 공격 타입이 Lunge Attack이 아니면 밀려나가는 거리 칸.
///
/// 유니티에는 "이 필드는 저 체크박스가 켜졌을 때만 만질 수 있다"를 선언할 방법이 없어서,
/// 기본 인스펙터를 직접 그리는 것 말고는 방법이 없다.
///
/// 필드를 하나씩 나열하지 않고 순회해서 그린다 — SkillData에 필드가 늘거나 순서가 바뀌어도
/// 여기를 같이 고칠 일이 없게 하기 위해서다. Header·Tooltip도 PropertyField가 그대로 살려준다.
///
/// 잠그는 건 어디까지나 인스펙터뿐이다. 실제로 그 값을 쓰지 않는 판단은
/// AttackCore가 따로 한다(칸에 값이 남아 있어도 쓰이지 않는다).
///
/// [위치] Editor 폴더 안이라 에디터 전용으로 컴파일된다. 빌드에는 들어가지 않는다.
/// </summary>
[CustomEditor(typeof(SkillData))]
public class SkillDataEditor : Editor
{
    /// <summary>잠금 조건과 잠글 필드 이름. SkillData 쪽 이름과 정확히 같아야 한다.</summary>
    const string LauncherFieldName = "isLauncher";
    const string GroundLaunchFieldName = "groundLaunchPower";
    const string AttackTypeFieldName = "attackType";
    const string LungeDistanceFieldName = "lungeDistance";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty launcher = serializedObject.FindProperty(LauncherFieldName);
        SerializedProperty attackType = serializedObject.FindProperty(AttackTypeFieldName);

        SerializedProperty prop = serializedObject.GetIterator();

        // 첫 NextVisible(true)가 짚는 건 m_Script 칸이다. 기본 인스펙터처럼 잠긴 상태로 보여준다.
        if (prop.NextVisible(true))
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(prop);
        }

        // 이후는 최상위 필드만 훑는다(false). Vector3 같은 건 PropertyField가 알아서 한 줄로 그린다.
        while (prop.NextVisible(false))
        {
            bool locked = (prop.name == GroundLaunchFieldName
                           && launcher != null
                           && !launcher.boolValue)
                          // AttackType은 값을 따로 지정하지 않아 선언 순서가 곧 enumValueIndex다.
                          || (prop.name == LungeDistanceFieldName
                              && attackType != null
                              && attackType.enumValueIndex != (int)AttackType.LungeAttack);

            using (new EditorGUI.DisabledScope(locked))
                EditorGUILayout.PropertyField(prop);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
