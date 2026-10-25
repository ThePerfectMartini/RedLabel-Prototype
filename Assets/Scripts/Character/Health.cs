using UnityEngine;

/// <summary>
/// 캐릭터의 체력. 맞으면 AttackCore가 TakeDamage로 깎고, 0이 되면 StateMachine.Die로 죽인다.
/// 죽은 뒤 누운(LieDown) 상태로 despawnDelay초가 지나면 캐릭터 오브젝트를 통째로 없앤다.
///
/// 이 컴포넌트가 없는 캐릭터는 체력이 깎이지 않는다. 지금은 적에게만 붙인다.
///
/// [준비물] 계층 위쪽에 Character, 그 아래 어딘가에 StateMachine.
/// </summary>
public class Health : MonoBehaviour
{
    [Tooltip("최대 체력. 시작할 때 이 값으로 채운다.")]
    [Min(1f)]
    [SerializeField] private float maxHealth = 30f;

    [Tooltip("죽은 뒤 누운 채로 이 시간(초)이 지나면 사라진다. 띄워져 죽었으면 착지 클립이 끝나고 눕는 순간부터 센다.")]
    [Min(0f)]
    [SerializeField] private float despawnDelay = 2f;

    Character character;
    StateMachine stateMachine;

    float current;

    /// <summary>죽은 뒤 누워 있은 시간.</summary>
    float despawnTimer;

    /// <summary>남은 체력. 명중 로그에 찍는다.</summary>
    public float Current => current;

    /// <summary>최대 체력. 체력 바가 비율을 계산하는 데 쓴다.</summary>
    public float Max => maxHealth;

    /// <summary>
    /// 피해를 입었을 때 피해량과 함께 불린다. 죽인 타격에서도 불린다.
    /// 슈퍼아머 중엔 TakeHit이 불리지 않아서, 보스(BossBrain)는 이걸로 맞은 걸 안다.
    /// </summary>
    public event System.Action<float> Damaged;

    void Awake()
    {
        current = maxHealth;

        // StateMachine은 LogicCores 같은 형제 가지에 있다. DownedHurtbox와 같은 방식으로 찾는다.
        character = GetComponentInParent<Character>();
        if (character != null)
            stateMachine = character.GetComponentInChildren<StateMachine>();

        if (stateMachine == null)
        {
            Debug.LogError($"{name}: Character 또는 StateMachine을 찾지 못해 체력이 동작하지 않습니다.", this);
            enabled = false;
        }
    }

    /// <summary>체력을 깎는다. 0 이하가 되면 죽인다. 이미 죽었으면 아무것도 하지 않는다.</summary>
    public void TakeDamage(float amount)
    {
        if (stateMachine == null || stateMachine.IsDead) return;

        current -= amount;
        if (current <= 0f)
        {
            current = 0f;
            stateMachine.Die();
        }

        Damaged?.Invoke(amount);
    }

    void Update()
    {
        if (!stateMachine.IsDead || stateMachine.State != CharacterState.LieDown) return;

        despawnTimer += Time.deltaTime;
        if (despawnTimer >= despawnDelay)
            Destroy(character.gameObject);
    }
}
