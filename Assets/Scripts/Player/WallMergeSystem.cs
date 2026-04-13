using UnityEngine;
using Unity.Cinemachine;

public class WallMergeSystem : MonoBehaviour
{
    [Header("감지")]
    public float detectDistance = 1.2f;
    public LayerMask mergeableWallLayer;

    [Header("벽 위 이동")]
    public float wallMoveSpeed = 3.5f;

    [Header("코너 감지")]
    public float cornerCheckDistance = 1.2f;  // 코너 탐색 거리
    public float cornerCheckOffset   = 0.5f;  // 벽 끝에서 얼마나 앞을 보는지

    [Header("카메라")]
    public CinemachineCamera vcamWall;
    public CinemachineCamera vcamTop;
    public float wallCamDistance = 8f;
    public float cameraFollowSpeed = 5f;      // 카메라 따라오는 속도

    [Header("비주얼")]
    public Material silhouetteMaterial;
    private Material originalMaterial;
    private Renderer characterRenderer;

    // 상태
    private bool isMerged = false;
    private Vector3 wallNormal;
    private Vector3 wallRight;
    private Vector3 wallPoint;
    private readonly float wallOffset = 0.1f;

    // 카메라 목표 위치 (매 프레임 갱신)
    private Vector3 targetCamPos;
    private Vector3 targetCamLook;

    // 컴포넌트
    private CharacterController cc;
    private PlayerMovement movement;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();
        characterRenderer = GetComponentInChildren<Renderer>();
        originalMaterial = characterRenderer.material;
    }

    void Update()
    {
        if (!isMerged)
        {
            DetectWall();
            if (Input.GetKeyDown(KeyCode.R))
                TryMerge();
        }
        else
        {
            HandleWallMovement();
            UpdateWallCamera();   // 매 프레임 카메라 추적

            if (Input.GetKeyDown(KeyCode.R))
                Detach();
        }
    }

    // ──────────────────────────────────────────
    // 벽 감지 & 합체
    // ──────────────────────────────────────────

    void DetectWall()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward,
            out hit, detectDistance, mergeableWallLayer))
        {
            wallNormal = hit.normal;
            wallPoint  = hit.point;
        }
    }

    void TryMerge()
    {
        RaycastHit hit;
        if (!Physics.Raycast(transform.position, transform.forward,
            out hit, detectDistance, mergeableWallLayer)) return;

        wallNormal = hit.normal;
        wallPoint  = hit.point;
        wallRight  = Vector3.Cross(Vector3.up, wallNormal).normalized;

        EnterMergeState();
    }

    void EnterMergeState()
    {
        isMerged = true;
        movement.isMerged = true;

        cc.enabled = false;
        transform.position = wallPoint + wallNormal * wallOffset;
        transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);

        if (silhouetteMaterial != null)
            characterRenderer.material = silhouetteMaterial;

        // 카메라 초기 위치 설정
        RefreshCameraTarget();
        vcamWall.transform.position = targetCamPos;
        vcamWall.transform.LookAt(targetCamLook);
        vcamWall.Priority = 20;
    }

    // ──────────────────────────────────────────
    // 카메라 — 매 프레임 플레이어 따라오기
    // ──────────────────────────────────────────

    void RefreshCameraTarget()
    {
        // 벽 법선 방향으로 일정 거리 + 플레이어 높이 기준
        targetCamPos  = transform.position + wallNormal * wallCamDistance;
        targetCamPos.y = transform.position.y + 2f;
        targetCamLook = transform.position;
    }

    void UpdateWallCamera()
    {
        RefreshCameraTarget();

        // 부드럽게 따라오기
        vcamWall.transform.position = Vector3.Lerp(
            vcamWall.transform.position,
            targetCamPos,
            cameraFollowSpeed * Time.deltaTime);

        vcamWall.transform.LookAt(targetCamLook);
    }

    // ──────────────────────────────────────────
    // 벽 위 이동 + 코너 감지
    // ──────────────────────────────────────────

    void HandleWallMovement()
    {
        float input = Input.GetAxisRaw("Horizontal");

        if (Vector3.Dot(Camera.main.transform.right, wallRight) < 0)
            input = -input;

        transform.position += wallRight * input * wallMoveSpeed * Time.deltaTime;

        // 현재 벽에 붙어있는지 확인
        RaycastHit hit;
        bool onWall = Physics.Raycast(
            transform.position + wallNormal * 0.5f,
            -wallNormal, out hit, 1.0f, mergeableWallLayer);

        if (onWall)
        {
            transform.position = hit.point + wallNormal * wallOffset;
            wallPoint = hit.point;

            // ★ 내측 코너 감지 — 벽에 붙어있는 중에도 매 프레임 체크
            if (input != 0)
                CheckInnerCornerOverlap();
        }
        else
        {
            // 기존 외측 코너 로직 (건드리지 않음)
            if (input != 0)
                TryCornerTransition(input > 0 ? wallRight : -wallRight);
            else
                Detach();
        }
    }

    void CheckInnerCornerOverlap()
    {
        // CharacterController 기본 캡슐 반지름 기준
        // Inspector에서 설정한 값과 맞춰야 함 (기본 0.4)
        float capsuleRadius = 0.4f;
        float overlapThreshold = capsuleRadius * 0.2f; // 반지름의 20%

        Collider[] nearby = Physics.OverlapSphere(
            transform.position, capsuleRadius + overlapThreshold, mergeableWallLayer);

        foreach (var col in nearby)
        {
            // 가장 가까운 표면 지점 구하기
            Vector3 closestPoint = col.ClosestPoint(transform.position);
            float dist = Vector3.Distance(transform.position, closestPoint);

            // 20% 이내로 근접한 벽만 처리
            if (dist > overlapThreshold) continue;

            // 이 벽의 안쪽 법선 구하기
            // Cube 기준 transform.forward / -forward 중 플레이어를 향하는 면 선택
            Vector3 toPlayer = (transform.position - closestPoint).normalized;

            // 후보 법선 4개 (Cube의 4개 옆면)
            Vector3[] candidates =
            {
                col.transform.forward,
                -col.transform.forward,
                col.transform.right,
                -col.transform.right
            };

            Vector3 bestNormal = Vector3.zero;
            float   bestDot    = -1f;

            foreach (var n in candidates)
            {
                float d = Vector3.Dot(n, toPlayer);
                if (d > bestDot)
                {
                    bestDot    = d;
                    bestNormal = n;
                }
            }

            // 현재 벽과 같은 방향이면 스킵 (현재 벽 재감지)
            if (Vector3.Angle(wallNormal, bestNormal) < 10f) continue;

            // 완전히 반대 방향이면 스킵 (등 뒤 벽)
            if (Vector3.Angle(wallNormal, bestNormal) > 170f) continue;

            Debug.Log($"내측 코너 감지 → {col.gameObject.name} / 법선: {bestNormal}");

            // 새 벽으로 전환
            wallNormal = bestNormal;
            wallPoint  = closestPoint;
            wallRight  = Vector3.Cross(Vector3.up, wallNormal).normalized;

            transform.position = wallPoint + wallNormal * wallOffset;
            transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);
            return; // 한 번만 전환
        }
    }

    void TryCornerTransition(Vector3 moveDir)
    {
        Collider[] nearby = Physics.OverlapSphere(
            transform.position, 1.8f, mergeableWallLayer);

        Debug.Log($"코너 탐색 — 주변 감지된 벽 수: {nearby.Length}");

        RaycastHit bestHit    = default;
        float      bestDist   = float.MaxValue;
        bool       foundWall  = false;

        foreach (var col in nearby)
        {
            // 현재 붙어있는 벽이면 스킵
            if (Vector3.Angle(wallNormal,
                (col.transform.position - transform.position).normalized) > 170f) 
            {
                // 현재 벽 콜라이더인지 확인 (법선 비교)
            }

            Vector3 closestPoint = col.ClosestPoint(transform.position);
            float dist = Vector3.Distance(transform.position, closestPoint);

            // ─────────────────────────────────────────────
            // 핵심 변경: 바깥에서 안쪽으로 Ray를 쏴서 법선 획득
            // 이동 방향 + 현재 wallNormal 반대 방향으로 오프셋한 위치에서 발사
            // ─────────────────────────────────────────────
            Vector3 rayOrigin = closestPoint
                            + (-wallNormal) * 0.5f   // 벽 바깥쪽으로
                            + moveDir      * 0.3f;   // 이동 방향으로 살짝

            Vector3 rayDir = (closestPoint - rayOrigin).normalized;

            RaycastHit hit;
            if (!Physics.Raycast(rayOrigin, rayDir, out hit, 2.0f, mergeableWallLayer))
                continue;

            // 현재 벽과 같은 법선이면 스킵 (현재 벽 재감지)
            if (Vector3.Angle(wallNormal, hit.normal) < 10f) continue;

            // 완전히 반대 방향 벽 스킵
            if (Vector3.Angle(wallNormal, hit.normal) > 170f) continue;

            // 가장 가까운 후보 선택
            if (dist < bestDist)
            {
                bestDist  = dist;
                bestHit   = hit;
                foundWall = true;
            }
        }

        if (foundWall)
            TransitionToNewWall(bestHit);
        else
            Detach();
    }

    void TransitionToNewWall(RaycastHit newWallHit)
    {
        Vector3 newNormal = newWallHit.normal;

        // 법선이 플레이어 방향을 향하는지 확인
        // 안쪽 코너에서 법선이 뒤집혀있으면 강제로 보정
        Vector3 toPlayer = (transform.position - newWallHit.point).normalized;
        if (Vector3.Dot(newNormal, toPlayer) < 0)
        {
            Debug.Log("법선 반전 보정 (안쪽 코너)");
            newNormal = -newNormal;
        }

        wallNormal = newNormal;
        wallPoint  = newWallHit.point;
        wallRight  = Vector3.Cross(Vector3.up, wallNormal).normalized;

        transform.position = wallPoint + wallNormal * wallOffset;
        transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);
    }

    // ──────────────────────────────────────────
    // 탈출
    // ──────────────────────────────────────────

    public void Detach()
    {
        if (!isMerged) return;

        isMerged = false;
        movement.isMerged = false;

        transform.position += wallNormal * 0.8f;
        cc.enabled = true;

        if (originalMaterial != null)
            characterRenderer.material = originalMaterial;

        vcamWall.Priority = 1;
    }

    // ──────────────────────────────────────────
    // 디버그 — Scene 뷰에서 Ray 시각화
    // ──────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!isMerged) return;

        // 현재 벽 유지 Ray (초록)
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position + wallNormal * 0.5f, -wallNormal);

        // 코너 탐색 Ray (노랑)
        Gizmos.color = Color.yellow;
        Vector3 originR = transform.position + wallRight * cornerCheckOffset;
        Vector3 originL = transform.position - wallRight * cornerCheckOffset;
        Gizmos.DrawRay(originR, -wallNormal * cornerCheckDistance);
        Gizmos.DrawRay(originR,  wallRight * cornerCheckDistance);
        Gizmos.DrawRay(originL, -wallNormal * cornerCheckDistance);
        Gizmos.DrawRay(originL, -wallRight * cornerCheckDistance);
    }
}