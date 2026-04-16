using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("이동")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 720f;

    [Header("가속/감속")]
    public float acceleration = 15f;
    public float deceleration = 20f;

    [HideInInspector] public bool isMerged = false;

    private CharacterController cc;
    private Vector3 currentVelocity;
    private Animator animator;

    // Animator 파라미터 해시 (문자열 비교보다 성능 우수)
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        cc = GetComponent<CharacterController>();

        // MaleCharacterPBR 같은 자식 오브젝트에 있는 Animator 자동 탐색
        animator = GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning("[PlayerMovement] Animator를 찾지 못했습니다. " +
                             "MaleCharacterPBR이 Player의 자식인지 확인하세요.");
    }

    void Update()
    {
        if (isMerged)
        {
            // 벽 합체 중에는 Speed를 0으로 수렴시켜 Idle 유지
            if (animator != null)
                animator.SetFloat(SpeedHash, 0f, 0.1f, Time.deltaTime);
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v).normalized;

        // 목표 속도 계산
        Vector3 targetVelocity = inputDir * moveSpeed;

        // 가속/감속 처리
        float rate = (inputDir.magnitude > 0.1f) ? acceleration : deceleration;
        currentVelocity = Vector3.MoveTowards(
            currentVelocity, targetVelocity, rate * Time.deltaTime);

        // 이동 적용
        cc.Move(currentVelocity * Time.deltaTime);

        // ★ 애니메이션 Speed 파라미터 업데이트
        // dampTime 0.1f: 급격한 전환 없이 부드럽게 블렌딩됨
        if (animator != null)
            animator.SetFloat(SpeedHash, currentVelocity.magnitude, 0.1f, Time.deltaTime);

        // 캐릭터 회전 — 이동 방향 바라보기
        if (inputDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(inputDir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
        }
    }
}