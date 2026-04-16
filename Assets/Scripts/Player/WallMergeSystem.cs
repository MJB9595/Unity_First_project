using UnityEngine;
using System.Collections.Generic;
using Unity.Cinemachine;

public class WallMergeSystem : MonoBehaviour
{
    [Header("감지")]
    public float detectDistance = 1.2f;
    public LayerMask mergeableWallLayer;

    [Header("벽 위 이동")]
    public float wallMoveSpeed = 3.5f;

    [Header("코너 감지")]
    public float cornerCheckDistance = 1.2f;
    public float cornerCheckOffset   = 0.5f;

    [Header("카메라")]
    public CinemachineCamera vcamWall;
    public CinemachineCamera vcamTop;
    public float wallCamDistance = 8f;
    public float cameraFollowSpeed = 5f;

    // ★ Inspector에서 MaleCharacterPBR을 직접 드래그해서 연결하세요.
    // 비워두면 Player 자신을 제외한 자식 Renderer를 자동 탐색합니다.
    [Header("3D 모델")]
    public GameObject characterModel;

    // 숨길 렌더러 목록 (Player 자체의 MeshRenderer는 제외)
    private Renderer[] modelRenderers;

    // 상태
    private bool isMerged = false;
    private Vector3 wallNormal;
    private Vector3 wallRight;
    private Vector3 wallPoint;
    private readonly float wallOffset = 0.1f;

    // 카메라 목표
    private Vector3 targetCamPos;
    private Vector3 targetCamLook;

    // 컴포넌트
    private CharacterController cc;
    private PlayerMovement movement;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();

        // ★ 렌더러 수집 — Player 자신에 붙은 Renderer는 반드시 제외
        if (characterModel != null)
        {
            // Inspector에서 MaleCharacterPBR을 직접 연결한 경우
            modelRenderers = characterModel.GetComponentsInChildren<Renderer>(true);
        }
        else
        {
            // 자동 탐색: Player 자신의 Renderer는 건드리지 않음
            var list = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject != gameObject)   // Player 루트 제외
                    list.Add(r);
            }
            modelRenderers = list.ToArray();

            if (modelRenderers.Length == 0)
                Debug.LogWarning("[WallMergeSystem] 숨길 Renderer를 찾지 못했습니다. " +
                                 "Inspector에서 Character Model 필드에 MaleCharacterPBR을 연결하세요.");
        }
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
            UpdateWallCamera();

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

        // ★ renderer.enabled = false 방식으로 숨기기
        // SetActive(false)와 달리 CharacterController, 스크립트, R키 입력 모두 유지됨
        SetModelVisible(false);

        // 카메라 초기화
        RefreshCameraTarget();
        vcamWall.transform.position = targetCamPos;
        vcamWall.transform.LookAt(targetCamLook);
        vcamWall.Priority = 20;
    }

    // ──────────────────────────────────────────
    // 카메라
    // ──────────────────────────────────────────

    void RefreshCameraTarget()
    {
        targetCamPos   = transform.position + wallNormal * wallCamDistance;
        targetCamPos.y = transform.position.y + 2f;
        targetCamLook  = transform.position;
    }

    void UpdateWallCamera()
    {
        RefreshCameraTarget();

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

        RaycastHit hit;
        bool onWall = Physics.Raycast(
            transform.position + wallNormal * 0.5f,
            -wallNormal, out hit, 1.0f, mergeableWallLayer);

        if (onWall)
        {
            transform.position = hit.point + wallNormal * wallOffset;
            wallPoint = hit.point;

            if (input != 0)
                CheckInnerCornerOverlap();
        }
        else
        {
            if (input != 0)
                TryCornerTransition(input > 0 ? wallRight : -wallRight);
            else
                Detach();
        }
    }

    void CheckInnerCornerOverlap()
    {
        float capsuleRadius    = 0.4f;
        float overlapThreshold = capsuleRadius * 0.2f;

        Collider[] nearby = Physics.OverlapSphere(
            transform.position, capsuleRadius + overlapThreshold, mergeableWallLayer);

        foreach (var col in nearby)
        {
            Vector3 closestPoint = col.ClosestPoint(transform.position);
            float dist = Vector3.Distance(transform.position, closestPoint);

            if (dist > overlapThreshold) continue;

            Vector3 toPlayer = (transform.position - closestPoint).normalized;

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
                if (d > bestDot) { bestDot = d; bestNormal = n; }
            }

            if (Vector3.Angle(wallNormal, bestNormal) < 10f)  continue;
            if (Vector3.Angle(wallNormal, bestNormal) > 170f) continue;

            wallNormal = bestNormal;
            wallPoint  = closestPoint;
            wallRight  = Vector3.Cross(Vector3.up, wallNormal).normalized;
            transform.position = wallPoint + wallNormal * wallOffset;
            transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);
            return;
        }
    }

    void TryCornerTransition(Vector3 moveDir)
    {
        Collider[] nearby = Physics.OverlapSphere(
            transform.position, 1.8f, mergeableWallLayer);

        RaycastHit bestHit   = default;
        float      bestDist  = float.MaxValue;
        bool       foundWall = false;

        foreach (var col in nearby)
        {
            Vector3 closestPoint = col.ClosestPoint(transform.position);
            float dist = Vector3.Distance(transform.position, closestPoint);

            Vector3 rayOrigin = closestPoint + (-wallNormal) * 0.5f + moveDir * 0.3f;
            Vector3 rayDir    = (closestPoint - rayOrigin).normalized;

            RaycastHit hit;
            if (!Physics.Raycast(rayOrigin, rayDir, out hit, 2.0f, mergeableWallLayer)) continue;
            if (Vector3.Angle(wallNormal, hit.normal) < 10f)  continue;
            if (Vector3.Angle(wallNormal, hit.normal) > 170f) continue;

            if (dist < bestDist) { bestDist = dist; bestHit = hit; foundWall = true; }
        }

        if (foundWall) TransitionToNewWall(bestHit);
        else           Detach();
    }

    void TransitionToNewWall(RaycastHit newWallHit)
    {
        Vector3 newNormal = newWallHit.normal;
        Vector3 toPlayer  = (transform.position - newWallHit.point).normalized;
        if (Vector3.Dot(newNormal, toPlayer) < 0) newNormal = -newNormal;

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

        isMerged          = false;
        movement.isMerged = false;

        transform.position += wallNormal * 0.8f;
        cc.enabled = true;

        // ★ 렌더러 다시 표시
        SetModelVisible(true);

        vcamWall.Priority = 1;
    }

    // ──────────────────────────────────────────
    // 렌더러 표시/숨김 헬퍼
    // ──────────────────────────────────────────

    void SetModelVisible(bool visible)
    {
        foreach (var r in modelRenderers)
            if (r != null) r.enabled = visible;
    }

    // ──────────────────────────────────────────
    // 디버그
    // ──────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!isMerged) return;

        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position + wallNormal * 0.5f, -wallNormal);

        Gizmos.color = Color.yellow;
        Vector3 originR = transform.position + wallRight * cornerCheckOffset;
        Vector3 originL = transform.position - wallRight * cornerCheckOffset;
        Gizmos.DrawRay(originR, -wallNormal * cornerCheckDistance);
        Gizmos.DrawRay(originR,  wallRight  * cornerCheckDistance);
        Gizmos.DrawRay(originL, -wallNormal * cornerCheckDistance);
        Gizmos.DrawRay(originL, -wallRight  * cornerCheckDistance);
    }
}