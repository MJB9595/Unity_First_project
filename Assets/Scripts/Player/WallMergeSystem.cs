// ================================================================
// WallMergeSystem.cs  (스텐실 기반 2D 스프라이트 렌더링 버전)
//
// 변경 사항 요약:
//   [추가] stencilMaskMaterial  — WallStencilMask 셰이더 마테리얼 연결
//   [추가] spriteMaterial       — PlayerSpriteStencil 셰이더 마테리얼 연결
//   [추가] spriteTexture        — 플레이어 2D 스프라이트 텍스처 연결
//   [추가] spriteScale          — 스프라이트 크기 조절
//   [내부] stencilMaskInstance  — 현재 합체 벽 위에 올라갈 투명 마스크 오브젝트
//   [내부] spriteQuad           — 벽 위에서 이동할 스프라이트 Quad 오브젝트
//
// Inspector 세팅 순서:
//   1. WallStencilMask.shader 로 Material 생성 → stencilMaskMaterial 에 연결
//   2. PlayerSpriteStencil.shader 로 Material 생성 → spriteMaterial 에 연결
//   3. spriteMaterial 의 _MainTex 에 플레이어 스프라이트 텍스처 연결
//   4. 기존과 동일하게 characterModel, vcamWall, vcamTop 등 연결
// ================================================================

using UnityEngine;
using System.Collections.Generic;
using Unity.Cinemachine;

public class WallMergeSystem : MonoBehaviour
{
    // ──────────────────────────────────────────
    // Inspector 필드
    // ──────────────────────────────────────────

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
    public float wallCamDistance  = 8f;
    public float cameraFollowSpeed = 5f;

    [Header("3D 모델")]
    // Inspector에서 MaleCharacterPBR을 직접 드래그해서 연결하세요.
    public GameObject characterModel;

    // ──────────────────────────────────────────
    // [NEW] 스텐실 / 2D 스프라이트 설정
    // ──────────────────────────────────────────

    [Header("2D 스프라이트 (WallMerge 시 표시)")]

    [Tooltip("WallStencilMask.shader 로 만든 Material을 연결하세요.\n" +
             "벽의 형태를 스텐실 버퍼에 찍어서 스프라이트 렌더 범위를 제한합니다.")]
    public Material stencilMaskMaterial;

    [Tooltip("PlayerSpriteStencil.shader 로 만든 Material을 연결하세요.\n" +
             "스텐실이 찍힌 영역(=현재 합체된 벽)에서만 렌더링됩니다.")]
    public Material spriteMaterial;

    [Tooltip("플레이어 2D 스프라이트 텍스처 (PNG, 투명 배경 권장)")]
    public Texture2D spriteTexture;

    [Tooltip("스프라이트 오브젝트의 크기. 캐릭터 비율에 맞게 조절하세요.")]
    public Vector2 spriteScale = new Vector2(1f, 2f);

    // ──────────────────────────────────────────
    // 내부 상태
    // ──────────────────────────────────────────

    private bool    isMerged   = false;
    private Vector3 wallNormal;
    private Vector3 wallRight;
    private Vector3 wallPoint;
    private readonly float wallOffset = 0.1f;

    // 카메라 목표
    private Vector3 targetCamPos;
    private Vector3 targetCamLook;

    // 컴포넌트
    private CharacterController cc;
    private PlayerMovement      movement;
    private Renderer[]          modelRenderers;

    // [NEW] 스텐실 & 스프라이트 런타임 오브젝트
    private GameObject stencilMaskInstance; // 합체된 벽 위에 올라가는 투명 마스크
    private GameObject spriteQuad;          // 플레이어를 따라 이동하는 스프라이트 Quad

    // ──────────────────────────────────────────
    // 초기화
    // ──────────────────────────────────────────

    void Awake()
    {
        cc       = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();

        // 숨길 렌더러 수집 (Player 루트 자신 제외)
        if (characterModel != null)
        {
            modelRenderers = characterModel.GetComponentsInChildren<Renderer>(true);
        }
        else
        {
            var list = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r.gameObject != gameObject)
                    list.Add(r);
            modelRenderers = list.ToArray();

            if (modelRenderers.Length == 0)
                Debug.LogWarning("[WallMergeSystem] 숨길 Renderer를 찾지 못했습니다. " +
                                 "Inspector에서 Character Model 필드에 MaleCharacterPBR을 연결하세요.");
        }

        // [NEW] 스텐실 마스크 오브젝트 미리 생성 (비활성 상태로 대기)
        InitStencilMask();

        // [NEW] 스프라이트 Quad 오브젝트 미리 생성 (비활성 상태로 대기)
        InitSpriteQuad();
    }

    // ──────────────────────────────────────────
    // [NEW] 초기화 헬퍼
    // ──────────────────────────────────────────

    /// <summary>
    /// 벽의 형태를 복사해서 스텐실 버퍼에 도장을 찍을 투명 오브젝트를 생성합니다.
    /// 합체 시 현재 벽의 Mesh / Transform 을 그대로 복사해서 사용합니다.
    /// </summary>
    void InitStencilMask()
    {
        if (stencilMaskMaterial == null)
        {
            Debug.LogWarning("[WallMergeSystem] stencilMaskMaterial 이 비어있습니다! " +
                             "Inspector에서 WallStencilMask 마테리얼을 연결하세요.");
            return;
        }

        stencilMaskInstance = new GameObject("_ActiveWall_StencilMask");
        stencilMaskInstance.AddComponent<MeshFilter>();

        var mr = stencilMaskInstance.AddComponent<MeshRenderer>();
        mr.material         = stencilMaskMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows   = false;

        stencilMaskInstance.SetActive(false);

        // 씬 루트에 배치 (Player 자식으로 두면 위치가 틀어질 수 있음)
        DontDestroyOnLoad(stencilMaskInstance);
    }

    /// <summary>
    /// 플레이어의 2D 스프라이트를 표시할 Quad 를 생성합니다.
    /// Quad는 항상 벽 표면 위에 딱 붙어서 플레이어 위치를 따라 이동합니다.
    /// </summary>
    void InitSpriteQuad()
    {
        if (spriteMaterial == null)
        {
            Debug.LogWarning("[WallMergeSystem] spriteMaterial 이 비어있습니다! " +
                             "Inspector에서 PlayerSpriteStencil 마테리얼을 연결하세요.");
            return;
        }

        // Unity 기본 Quad 프리미티브 생성
        spriteQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        spriteQuad.name = "_PlayerSpriteQuad";

        // 콜라이더 제거 (물리 방해 금지)
        Destroy(spriteQuad.GetComponent<Collider>());

        // 스프라이트 마테리얼 적용
        var mr = spriteQuad.GetComponent<MeshRenderer>();
        mr.material = spriteMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        // 텍스처 설정
        if (spriteTexture != null)
            mr.material.SetTexture("_MainTex", spriteTexture);
        else
            Debug.LogWarning("[WallMergeSystem] spriteTexture 가 비어있습니다! " +
                             "Inspector에서 2D 스프라이트 텍스처를 연결하세요.");

        // 초기 크기 설정
        spriteQuad.transform.localScale = new Vector3(spriteScale.x, spriteScale.y, 1f);

        spriteQuad.SetActive(false);
        DontDestroyOnLoad(spriteQuad);
    }

    // ──────────────────────────────────────────
    // Update
    // ──────────────────────────────────────────

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

            // [NEW] 스프라이트 위치를 매 프레임 갱신
            UpdateSpritePosition();

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

        EnterMergeState(hit.collider.gameObject);
    }

    // [CHANGED] 합체 대상 벽 오브젝트를 인자로 받도록 변경
    void EnterMergeState(GameObject wallObject)
    {
        isMerged          = true;
        movement.isMerged = true;

        cc.enabled = false;
        transform.position = wallPoint + wallNormal * wallOffset;
        transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);

        // 3D 모델 숨기기
        SetModelVisible(false);

        // [NEW] 스텐실 마스크를 현재 벽에 맞게 설정
        ApplyStencilMask(wallObject);

        // [NEW] 스프라이트 Quad 활성화 및 초기 위치 설정
        if (spriteQuad != null)
        {
            spriteQuad.SetActive(true);
            UpdateSpritePosition();
        }

        // 카메라 초기화
        RefreshCameraTarget();
        vcamWall.transform.position = targetCamPos;
        vcamWall.transform.LookAt(targetCamLook);
        vcamWall.Priority = 20;
    }

    // ──────────────────────────────────────────
    // [NEW] 스텐실 마스크 적용
    // ──────────────────────────────────────────

    /// <summary>
    /// 현재 합체된 벽 오브젝트의 Mesh와 Transform을 스텐실 마스크 오브젝트에 복사합니다.
    /// 코너 이동으로 다른 벽으로 넘어갈 때도 이 함수를 호출해서 마스크를 갱신합니다.
    /// </summary>
    void ApplyStencilMask(GameObject wallObject)
    {
        if (stencilMaskInstance == null || wallObject == null) return;

        MeshFilter wallMeshFilter = wallObject.GetComponent<MeshFilter>();
        if (wallMeshFilter == null)
        {
            Debug.LogWarning($"[WallMergeSystem] {wallObject.name} 에 MeshFilter가 없습니다. " +
                             "스텐실 마스크를 적용할 수 없습니다.");
            stencilMaskInstance.SetActive(false);
            return;
        }

        // 벽과 동일한 Mesh / 위치 / 회전 / 크기를 마스크에 복사
        stencilMaskInstance.GetComponent<MeshFilter>().sharedMesh = wallMeshFilter.sharedMesh;
        stencilMaskInstance.transform.position   = wallObject.transform.position;
        stencilMaskInstance.transform.rotation   = wallObject.transform.rotation;
        stencilMaskInstance.transform.localScale = wallObject.transform.localScale;

        stencilMaskInstance.SetActive(true);
    }

    // ──────────────────────────────────────────
    // [NEW] 스프라이트 위치 갱신
    // ──────────────────────────────────────────

    /// <summary>
    /// 스프라이트 Quad를 플레이어 위치에 맞춰 벽 표면 위에 배치합니다.
    /// - 위치: 플레이어와 동일하되 wallNormal 방향으로 살짝 앞으로 (z-fighting 방지)
    /// - 회전: 벽의 법선 반대 방향을 바라봄 (= 카메라 쪽을 향함)
    /// </summary>
    void UpdateSpritePosition()
    {
        if (spriteQuad == null || !spriteQuad.activeSelf) return;

        // 벽 표면에서 0.02f 만큼 앞으로 띄워서 z-fighting 방지
        spriteQuad.transform.position = transform.position + wallNormal * 0.02f;

        // 스프라이트가 벽 바깥(= 카메라 방향)을 바라보도록 회전
        spriteQuad.transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);
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

            // [NEW] 코너를 넘어갈 때 새 벽에 스텐실 마스크 갱신
            ApplyStencilMask(col.gameObject);

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

        RaycastHit bestHit  = default;
        float      bestDist = float.MaxValue;
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

        // [NEW] 새로운 벽에 스텐실 마스크 갱신
        ApplyStencilMask(newWallHit.collider.gameObject);

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

        // 3D 모델 복구
        SetModelVisible(true);

        // [NEW] 스텐실 마스크 & 스프라이트 비활성화
        if (stencilMaskInstance != null)
            stencilMaskInstance.SetActive(false);
        if (spriteQuad != null)
            spriteQuad.SetActive(false);

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