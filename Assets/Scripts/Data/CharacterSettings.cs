using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 캐릭터의 설정값을 담는 에셋. 씬이나 프리팹이 아니라 프로젝트 파일로 존재해서,
/// 여러 캐릭터가 같은 에셋 하나를 공유하거나 캐릭터마다 다른 에셋을 물릴 수 있다.
///
/// [만드는 법] Project 창 우클릭 > Create > 캐릭터 > 캐릭터 셋팅
/// </summary>
[CreateAssetMenu(fileName = "CharacterSettings", menuName = "캐릭터/캐릭터 셋팅")]
public class CharacterSettings : ScriptableObject
{
    [Header("이동")]
    [Tooltip("초당 이동 거리")]
    public float moveSpeed = 5f;

    [Tooltip("달릴 때의 초당 이동 거리. 좌/우 이동키를 빠르게 두 번 눌러 유지하면 이 속도로 달린다.")]
    public float runSpeed = 9f;

    [Tooltip("지상에서 초당 줄어드는 수평 속도. 넉백처럼 입력이 아닌 힘으로 얻은 속도에만 걸린다. " +
             "밀리는 거리는 스킬의 지상 넉백 파워를 제곱해서 (2 x 이 값)으로 나눈 만큼이다.")]
    public float groundFriction = 20f;

    [Header("점프")]
    [Tooltip("점프하는 순간 위로 솟구치는 힘. 클수록 높이 뜬다.")]
    public float jumpPower = 8f;

    [Tooltip("캐릭터의 무게. 1이 보통이고, 클수록 빨리 떨어져서 점프가 낮고 짧아진다. " +
             "날렵하고 가벼운 캐릭터는 0.7, 육중한 캐릭터는 1.5처럼 준다.")]
    public float weight = 1f;

    [Header("튕김")]
    [Tooltip("맞아서 떴다가 이 높이(m) 이상 떨어지면 바닥에서 다시 튕겨 오른다. 이보다 낮으면 바로 착지한다.")]
    public float bounceMinHeight = 1.5f;

    [Tooltip("무게 1 기준으로, 떨어진 높이 대비 튕겨 오르는 높이의 비율. 실제로는 무게로 나눠서 무거울수록 낮게 튄다. " +
             "나눈 값이 0.9를 넘으면 0.9로 막는다 — 튕길 때마다 낮아져야 언젠가 멈추기 때문이다.")]
    public float bounceRatio = 0.3f;

    [Tooltip("튕길 때마다 수평 속도에 곱하는 값. 0.5면 한 번 튈 때마다 절반으로 느려지고, 0이면 제자리에서 위로만 튄다.")]
    public float bounceHorizontalDamping = 0.5f;

    [Header("공격")]
    [Tooltip("X키로 나가는 기본 공격.")]
    [FormerlySerializedAs("attack1")]
    public SkillData basicAttack;

    [Tooltip("Z키로 나가는 특수 공격.")]
    [FormerlySerializedAs("attack2")]
    public SkillData specialAttack;

    [Tooltip("달리는 중에 X키로 나가는 대시 공격. 비워두면 달리는 중에도 기본 공격이 나간다.")]
    public SkillData dashAttack;

    [Tooltip("공중에서 X키로 나가는 점프 공격. 비워두면 공중에서도 기본 공격이 나간다.")]
    public SkillData jumpAttack;
}