using UnityEngine;

/// <summary>
/// MovementCore가 들고 있는 좌/우 방향을 스프라이트에 반영하는 컴포넌트.
///
/// 방향 자체는 여기서 정하지 않는다. 이동·공격·피격 중 방향이 언제 바뀌고 언제 고정되는지는
/// MovementCore가 이미 정하고 있으므로, 여기서는 그 결과를 그림에 옮기기만 한다.
///
/// flipX는 그림만 뒤집고 위치는 그대로 둔다. 그래서 몸 중심에서 비껴 놓은 Visual의 오프셋도
/// 같이 거울처럼 뒤집는다 — 안 그러면 왼쪽을 볼 때 스프라이트가 몸에서 어긋난다.
///
/// 원본 스프라이트가 오른쪽을 보고 있다고 전제한다.
///
/// [준비물] 이 오브젝트에 SpriteRenderer, 계층 위쪽에 Character, 그 아래 어딘가에 MovementCore.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFacing : MonoBehaviour
{
    SpriteRenderer spriteRenderer;
    MovementCore movement;

    /// <summary>오른쪽을 볼 때의 localPosition.x. 왼쪽을 볼 때는 이 값의 부호를 뒤집는다.</summary>
    float baseOffsetX;

    /// <summary>마지막으로 반영한 방향. 바뀐 프레임에만 값을 쓰기 위해 기억한다.</summary>
    bool appliedFacingRight = true;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // MovementCore는 LogicCores 같은 형제 가지에 있다. AnimationEventRelay와 같은 방식으로 찾는다.
        Character character = GetComponentInParent<Character>();
        if (character != null)
            movement = character.GetComponentInChildren<MovementCore>();

        if (movement == null)
        {
            Debug.LogWarning($"{name}: MovementCore를 찾지 못해 스프라이트 방향을 바꿀 수 없습니다.", this);
            enabled = false;
            return;
        }

        // 씬에 놓인 그대로가 '오른쪽을 볼 때'의 모습이다.
        baseOffsetX = transform.localPosition.x;
    }

    // 이동이 Update에서 방향을 정한 뒤에 그린다. 같은 프레임 안에서 한 박자 늦게 뒤집히는 걸 막는다.
    void LateUpdate()
    {
        bool facingRight = movement.FacingDir.x > 0f;
        if (facingRight == appliedFacingRight) return;

        appliedFacingRight = facingRight;
        spriteRenderer.flipX = !facingRight;

        Vector3 pos = transform.localPosition;
        pos.x = facingRight ? baseOffsetX : -baseOffsetX;
        transform.localPosition = pos;
    }
}
