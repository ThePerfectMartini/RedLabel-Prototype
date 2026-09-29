using UnityEngine;

/// <summary>
/// 캐릭터 한 명을 대표하는 컴포넌트. 계층의 최상위(Player, Enemy 등)에 붙인다.
///
/// 두 가지 일을 한다:
///  1. 셋팅 에셋의 주인. 아래 Core들이 여기서 받아간다.
///  2. 이 캐릭터의 '몸'. Core들은 자기 transform이 아니라 이 transform을 움직이고,
///     타격 판정도 이 위치를 기준으로 낸다.
///
/// Core를 LogicCores 같은 자식으로 내려도 계층 어디에 있든 GetComponentInParent로 같은
/// 기준점을 찾아오게 하려고 둔다. "이 콜라이더가 누구 것인가"를 판단하는 기준이기도 하다.
///
/// [준비물] 이 오브젝트 아래 어딘가에 Collider 하나. Core들은 아래 어디에 있어도 된다.
/// </summary>
public class Character : MonoBehaviour
{
    [Tooltip("이 캐릭터가 쓸 설정 에셋. 비어 있으면 움직이지 않는다.")]
    [SerializeField] CharacterSettings settings;

    /// <summary>이 캐릭터의 설정 에셋. 아래 Core들이 여기서 꺼내 간다.</summary>
    public CharacterSettings Settings => settings;

    void Awake()
    {
        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (settings == null)
            Debug.LogError($"{name}: Character에 캐릭터 셋팅이 연결되지 않았습니다.", this);
    }
}