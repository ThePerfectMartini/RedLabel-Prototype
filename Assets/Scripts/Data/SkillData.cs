using UnityEngine;
using UnityEngine.Serialization;

/// <summary>공격하는 동안 시전자가 어떻게 움직이는지. SkillData의 공격 타입 드롭다운에 뜬다.</summary>
public enum AttackType
{
    /// <summary>그 자리에 멈춰서 때린다. 걷던 속도도 공격 시작 순간 끊긴다.</summary>
    LockedAttack,

    /// <summary>바라보는 방향으로 밀려나가며 때린다. 밀리는 거리는 SkillData.lungeDistance.</summary>
    LungeAttack,

    /// <summary>
    /// 공중에서 쓰는 공격. 시전자 이동을 전혀 건드리지 않는다 — 공중 이동은 MovementCore의 공중 조작이 맡는다.
    /// 에셋에는 순서 번호로 저장되므로 새 타입은 항상 맨 뒤에 붙인다.
    /// </summary>
    JumpAttack,
}

/// <summary>
/// 공격 한 종류가 갖는 모든 수치를 담는 에셋. 기본공격·특수공격처럼 종류마다 하나씩 만든다.
///
/// 캐릭터가 아니라 '공격'이 주인이다 — 같은 스킬을 여러 캐릭터가 공유할 수 있고,
/// 한 캐릭터가 여러 스킬을 들 수도 있다. 등록은 CharacterSettings의 공격 칸에서 한다.
///
/// 넉백 수치는 맞은 쪽이 지상이냐 공중이냐에 따라 두 벌로 나뉜다. 같은 공격이라도
/// 서 있는 상대를 띄우는 것과 이미 떠 있는 상대를 다루는 것은 원하는 결과가 다르기 때문이다.
///
/// [만드는 법] Project 창 우클릭 > Create > 캐릭터 > 스킬 데이터
/// </summary>
[CreateAssetMenu(fileName = "SkillData", menuName = "캐릭터/스킬 데이터")]
public class SkillData : ScriptableObject
{
    [Header("애니메이션")]
    [Tooltip("이 스킬이 재생할 Animator 상태 이름. 컨트롤러 쪽 상태 이름과 정확히 일치해야 한다. " +
             "동작 길이는 같은 이름의 클립에서 자동으로 읽으므로, 클립 이름도 같게 맞춰야 한다.")]
    public string animatorStateName = "Attack";

    [Header("콤보")]
    [Tooltip("공격 선입력 타이밍 직접설정. 켜면 클립의 OnComboWindowOpen 이벤트에서 선입력 창이 열리고, " +
             "그 이벤트가 없으면 OnAttackHitFrame과 동시에 열린다. " +
             "끄면 이 동작에는 선입력이 없고, 아래 다음 공격 칸도 비워진다.")]
    public bool useComboInputTiming = true;

    [Tooltip("이 동작 중에 같은 공격키를 다시 누르면 이어질 다음 공격. 비워두면 콤보가 여기서 끝난다. " +
             "선입력 창이 열린 뒤에 누르면 남은 동작을 끊고 즉시 시작되며, 그 전의 입력은 버려진다. " +
             "마지막 단계에 첫 단계를 넣으면 순환한다. 위의 선입력 타이밍 직접설정이 꺼져 있으면 쓰이지 않는다.")]
    public SkillData nextCombo;

    [Tooltip("기본공격 캔슬. 켜면 지상에서 기본공격 콤보가 도는 도중에 이 스킬을 쓰면, 그 동작을 즉시 끊고 " +
             "이 스킬이 나간다. 선입력 창과 무관하게 동작 도중 언제든 끊는다. " +
             "대시공격·점프공격이나 공중에서 시작한 공격은 끊지 않는다.")]
    public bool cancelsBasicAttack = false;

    [Header("시전자 이동")]
    [Tooltip("공격하는 동안 시전자가 어떻게 움직이는지. " +
             "Locked Attack은 그 자리에 멈춰서 때리고, Lunge Attack은 바라보는 방향으로 밀려나가며 때린다. " +
             "Jump Attack은 공중 공격용으로, 시전자 이동을 건드리지 않는다. " +
             "Locked·Lunge는 지상에서만 적용되고, 공중에서 공격하면 점프 중엔 방향키 조작이, 그 외엔 떠오를 때의 속도가 그대로 이어진다.")]
    public AttackType attackType = AttackType.LockedAttack;

    [Tooltip("Lunge Attack일 때 공격 시작 순간부터 바라보는 방향으로 밀려나가는 거리(m). " +
             "시전자의 groundFriction으로 서서히 멈추며, 마찰이 다른 캐릭터가 써도 가는 거리는 같다. " +
             "멈추기까지 걸리는 시간은 √(2 x 거리 ÷ groundFriction)초이고, 이게 공격 클립보다 길면 " +
             "공격이 끝나는 순간 남은 이동이 끊긴다. Locked Attack이면 쓰이지 않는다.")]
    [Min(0f)]
    [FormerlySerializedAs("dashDistance")]
    public float lungeDistance = 1f;

    [Header("판정")]
    [Tooltip("몸 중심에서 공격 판정 상자 중심까지의 거리. 팔이 닿는 거리라고 보면 된다.")]
    public float attackDistance = 1f;

    [Tooltip("몸 중심에서 공격 판정 상자 중심까지의 높이. 양수면 위로 올라간다. " +
             "상자가 땅속에 묻히지 않게 띄울 때 쓴다.")]
    public float attackHeight = 0f;

    [Tooltip("공격 판정 상자의 크기. X는 바라보는 쪽으로의 길이(리치), Y는 높이, Z는 좌우 폭.")]
    public Vector3 attackBoxSize = new Vector3(1.5f, 1f, 1f);

    [Header("효과")]
    [Tooltip("맞은 대상에게 주는 피해량. 아직 체력 시스템이 없어서 명중 로그에만 찍힌다.")]
    public float damage = 10f;

    [Tooltip("맞은 대상이 굳어 있는 시간(초). 이 동안 이동·공격이 막힌다. " +
             "체공 시간보다 짧으면 아직 날아가는 중에 경직이 풀린다. 지상·공중 공통이다.")]
    public float hitStunDuration = 0.8f;

    [Header("지상의 대상에게")]
    [Tooltip("서 있는 대상을 밀어내는 속도. 때린 쪽이 바라보는 방향으로 밀린다. " +
             "밀리는 거리는 이 값을 제곱해서 (2 x 맞은 쪽 groundFriction)으로 나눈 만큼이다.")]
    [FormerlySerializedAs("knockbackPower")]
    public float groundKnockbackPower = 5f;

    [Tooltip("띄우는 공격인지. 끄면 아래 런치 파워를 무시하고 바닥으로만 밀어낸다. " +
             "이미 떠 있는 대상에게는 이 체크와 무관하게 공중 런치 파워가 쓰인다.")]
    public bool isLauncher = false;

    [Tooltip("서 있는 대상을 띄우는 힘. jumpPower와 같은 단위다. " +
             "위의 '띄우는 공격'이 꺼져 있으면 이 값은 쓰이지 않는다.")]
    [FormerlySerializedAs("launchPower")]
    public float groundLaunchPower = 8f;

    [Header("공중의 대상에게")]
    [Tooltip("이미 떠 있는 대상을 밀어내는 속도. 공중에는 마찰이 없어서 같은 값이라도 지상보다 멀리 날아간다.")]
    public float airborneKnockbackPower = 7f;

    [Tooltip("이미 떠 있는 대상의 수직 속도를 이 값으로 덮어쓴다. 양수면 다시 솟고, 음수면 내려찍힌다. " +
             "0이면 수직에 손대지 않아서, 올라가던 대상은 계속 올라가고 떨어지던 대상은 계속 떨어진다.")]
    public float airborneLaunchPower = 4f;

    [Header("누운 대상에게")]
    [Tooltip("쓰러져 있는(착지·누움·일어서기) 대상을 밀어내는 속도. 바닥에 있으므로 지상처럼 마찰로 멈춘다.")]
    public float downedKnockbackPower = 5f;

    [Tooltip("쓰러져 있는 대상을 띄우는 힘. jumpPower와 같은 단위다. '띄우는 공격' 체크와 무관하게 늘 쓰인다. " +
             "0이면 바닥으로만 밀리고, 0보다 크면 다시 띄워진다.")]
    public float downedLaunchPower = 0f;

    // 선입력이 없으면 다음 단계로 넘어갈 길도 없으니, 인스펙터에 남아 헷갈리지 않게 비운다.
    void OnValidate()
    {
        if (!useComboInputTiming)
            nextCombo = null;
    }
}
