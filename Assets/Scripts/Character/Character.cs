using UnityEngine;

/// <summary>
/// 캐릭터의 진영. 같은 진영끼리는 공격이 맞지 않는다.
/// 에셋·씬에는 순서 번호로 저장되므로 새 진영은 항상 맨 뒤에 붙인다.
/// </summary>
public enum Team
{
    /// <summary>진영 없음. 누구든 때리고 누구에게든 맞는다(진영이 생기기 전의 동작).</summary>
    Neutral,

    Player,

    Enemy,
}

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

    [Tooltip("이 캐릭터의 진영. 같은 진영끼리는 공격이 맞지 않는다. Neutral은 진영이 없어 누구와도 맞는다.")]
    [SerializeField] Team team = Team.Neutral;

    /// <summary>이 캐릭터의 설정 에셋. 아래 Core들이 여기서 꺼내 간다.</summary>
    public CharacterSettings Settings => settings;

    /// <summary>이 캐릭터의 진영. AttackCore가 같은 편을 때리지 않으려고 읽는다.</summary>
    public Team Team => team;

    void Awake()
    {
        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (settings == null)
            Debug.LogError($"{name}: Character에 캐릭터 셋팅이 연결되지 않았습니다.", this);
    }
}