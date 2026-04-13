using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("이동")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 720f;

    [Header("가속/감속")]
    public float acceleration = 15f;   // 빠르게 가속
    public float deceleration = 20f;   // 빠르게 멈춤

    [HideInInspector] public bool isMerged = false;

    private CharacterController cc;
    private Vector3 currentVelocity;   // 현재 실제 속도 (부드럽게 변화)

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (isMerged) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v).normalized;

        // 목표 속도 계산
        Vector3 targetVelocity = inputDir * moveSpeed;

        // 가속/감속 부드럽게 처리
        float rate = (inputDir.magnitude > 0.1f) ? acceleration : deceleration;
        currentVelocity = Vector3.MoveTowards(
            currentVelocity, targetVelocity, rate * Time.deltaTime);

        // 이동 적용
        cc.Move(currentVelocity * Time.deltaTime);

        // 캐릭터 회전 — 이동 방향 바라보기
        if (inputDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(inputDir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
        }
    }
}