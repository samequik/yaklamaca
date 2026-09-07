using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Bağımsız görseli olan, taşınabilen ve kabine yerleştirilebilen sunucu ragdoll'u.</summary>
[RequireComponent(typeof(RagdollSync))]
public class Corpse : NetworkBehaviour, IInteractable
{
    [SyncVar] private uint victimNetId;
    [SyncVar] private string victimName;
    [SyncVar(hook = nameof(OnHolderChanged))] private uint carrierNetId;
    [SyncVar(hook = nameof(OnHolderChanged))] private uint stationNetId;
    [SerializeField] private GameObject bodyPrefab;
    [SerializeField] private string[] bonePaths;
    [SerializeField] private float ragdollMass = 70f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float pushStrength = 0.6f;
    [SerializeField] private float pushReach = 0.25f;
    private static readonly List<Corpse> all = new List<Corpse>();
    private readonly Dictionary<Transform, Vector3> lastPlayerPositions = new Dictionary<Transform, Vector3>();
    private readonly Collider[] nearbyPlayers = new Collider[16];
    private List<RagdollFactory.Part> ragdoll;
    private RagdollSync sync;
    private Renderer[] renderers;
    private Vector3[] heldOffsets;
    private Quaternion[] heldRotations;
    private int playerMask;
    public uint VictimNetId => victimNetId;
    public uint StationNetId => stationNetId;
    public string VictimName => victimName;
    public Vector3 Position => ragdoll != null && ragdoll.Count > 0 ? ragdoll[0].Bone.position : transform.position;
    public bool IsHeld => carrierNetId != 0 || stationNetId != 0;

    public static Corpse CarriedBy(RoundParticipant player)
    {
        if (player == null) return null;
        foreach (Corpse corpse in all)
            if (corpse != null && corpse.carrierNetId == player.netId) return corpse;
        return null;
    }
    public static bool LivingRunner(RoundParticipant player) => player != null && player.IsAlive
        && !player.IsEscaped && !player.IsSpectating && player.Role == RoundRole.Runner
        && RoundManager.Instance != null && RoundManager.Instance.Phase == RoundPhase.Playing;
    public static RoundParticipant Resolve(uint id)
    {
        NetworkIdentity identity;
        if (NetworkServer.active && NetworkServer.spawned.TryGetValue(id, out identity))
            return identity.GetComponent<RoundParticipant>();
        return NetworkClient.spawned.TryGetValue(id, out identity) ? identity.GetComponent<RoundParticipant>() : null;
    }
    private void Awake()
    {
        all.Add(this);
        sync = GetComponent<RagdollSync>();
        playerMask = LayerMask.GetMask("Oyuncu");
    }
    private void OnDestroy() => all.Remove(this);
    [Server] public void ServerInit(uint victim)
    {
        victimNetId = victim;
        var player = Resolve(victim);
        victimName = player != null ? player.DisplayName : "Kaçan";
    }
    public override void OnStartServer() { BuildVisual(); ApplyAuthority(); sync.Publish(); }
    public override void OnStartClient() { BuildVisual(); ApplyAuthority(); }

    private void BuildVisual()
    {
        if (ragdoll != null) return;
        if (bodyPrefab == null || bonePaths == null)
        {
            Debug.LogError("Ceset görseli bağlı değil: Diriltme Sistemini Kur aracını çalıştır.", this);
            return;
        }
        GameObject clone = Instantiate(bodyPrefab, transform.position, transform.rotation, transform);
        clone.SetActive(true);
        foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        // Sunucu ölüm klibinin son pozunu alır. İstemci kurban objesine ihtiyaç duymaz.
        RoundParticipant victim = isServer ? Resolve(victimNetId) : null;
        Transform source = victim != null ? victim.CorpseSourceBody : null;
        if (source != null) CopyPose(source, clone.transform);
        Transform ResolveBone(HumanBodyBones bone)
        {
            int index = (int)bone;
            if (index >= bonePaths.Length || string.IsNullOrEmpty(bonePaths[index])) return null;
            return clone.transform.Find(bonePaths[index]);
        }
        renderers = clone.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
        }
        ragdoll = RagdollFactory.Build(ResolveBone, ragdollMass, LayerMask.NameToLayer("Etkilesim"));
        if (ragdoll.Count == 0) { Debug.LogError("Ceset iskeleti kurulamadı.", this); return; }
        RagdollFactory.DisableSelfCollision(ragdoll);
        sync.Bind(ragdoll);
        ApplyAuthority();
    }
    private static void CopyPose(Transform source, Transform target, bool isRoot = true)
    {
        // Ölçek klonun dinlenme ölçeğinde kalır: birinci şahısta gizlenen kafa taşınmaz.
        if (!isRoot) target.localRotation = source.localRotation;
        foreach (Transform child in target)
        {
            Transform original = source.Find(child.name);
            if (original == null) continue;
            child.localPosition = original.localPosition;
            CopyPose(original, child, false);
        }
    }
    private void OnHolderChanged(uint before, uint after) => ApplyAuthority();
    private void ApplyAuthority()
    {
        if (ragdoll == null) return;
        foreach (var part in ragdoll)
        {
            bool simulate = isServer && !IsHeld;
            if (part.Body.isKinematic != !simulate)
            {
                part.Body.isKinematic = !simulate;
                if (simulate) { part.Body.velocity = Vector3.zero; part.Body.angularVelocity = Vector3.zero; part.Body.WakeUp(); }
            }
            part.Collider.enabled = !IsHeld;
        }
        sync.Continuous = IsHeld;
    }
    private void LateUpdate()
    {
        if (renderers == null) return;
        bool hide = NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == carrierNetId;
        foreach (Renderer renderer in renderers) renderer.enabled = !hide;
    }
    private void FixedUpdate()
    {
        if (!isServer || ragdoll == null || ragdoll.Count == 0) return;
        if (carrierNetId != 0)
        {
            RoundParticipant carrier = Resolve(carrierNetId);
            if (!LivingRunner(carrier) || carrier.GetComponent<PlayerController>()?.IsFocused == true)
            { ServerDrop(); return; }
            MoveHeld(carrier.transform.TransformPoint(new Vector3(0.45f, 0.65f, -0.1f)), carrier.transform.rotation);
            return;
        }
        if (stationNetId != 0) return;
        foreach (var part in ragdoll)
            if (part.Body.velocity.sqrMagnitude > maxSpeed * maxSpeed)
                part.Body.velocity = part.Body.velocity.normalized * maxSpeed;
        ShoveFromPlayers();
    }
    private void CaptureHeldPose(Quaternion orientation)
    {
        heldOffsets = new Vector3[ragdoll.Count]; heldRotations = new Quaternion[ragdoll.Count];
        Vector3 anchor = Position;
        Quaternion inverse = Quaternion.Inverse(orientation);
        for (int i = 0; i < ragdoll.Count; i++)
        {
            heldOffsets[i] = inverse * (ragdoll[i].Bone.position - anchor);
            heldRotations[i] = inverse * ragdoll[i].Bone.rotation;
        }
    }
    private void MoveHeld(Vector3 anchor, Quaternion orientation)
    {
        if (heldOffsets == null) CaptureHeldPose(orientation);
        for (int i = 0; i < ragdoll.Count; i++)
        {
            Vector3 position = anchor + orientation * heldOffsets[i];
            Quaternion rotation = orientation * heldRotations[i];
            ragdoll[i].Bone.SetPositionAndRotation(position, rotation);
            ragdoll[i].Body.position = position;
            ragdoll[i].Body.rotation = rotation;
        }
    }
    public string GetPrompt()
    {
        var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<RoundParticipant>() : null;
        return LivingRunner(local) && !IsHeld && CarriedBy(local) == null ? victimName + " — cesedi taşı" : null;
    }
    public void Interact(GameObject user) => CmdPickUp();
    [Command(requiresAuthority = false)]
    private void CmdPickUp(NetworkConnectionToClient sender = null)
    {
        var player = sender?.identity != null ? sender.identity.GetComponent<RoundParticipant>() : null;
        if (!LivingRunner(player) || IsHeld || CarriedBy(player) != null || ragdoll == null || ragdoll.Count == 0) return;
        if ((player.transform.position - Position).sqrMagnitude > 2.6f * 2.6f) return;
        if (!ClearReach(player.transform.position + Vector3.up * 0.8f, Position)) return;
        if (player.GetComponent<PlayerController>()?.IsFocused == true) return;
        CaptureHeldPose(player.transform.rotation);
        carrierNetId = player.netId;
        ApplyAuthority();
    }
    public void Drop() => CmdDrop();
    [Command(requiresAuthority = false)]
    private void CmdDrop(NetworkConnectionToClient sender = null)
    {
        if (sender?.identity != null && sender.identity.netId == carrierNetId) ServerDrop();
    }
    [Server] public void ServerDrop()
    {
        if (carrierNetId == 0) return;
        var carrier = Resolve(carrierNetId);
        if (carrier != null)
        {
            Vector3 anchor = carrier.transform.position + Vector3.up * 0.5f;
            // Bırakırken duvarın ötesine atma: taşıyıcının ayaklarının üstüne indir.
            MoveHeld(anchor, carrier.transform.rotation);
        }
        carrierNetId = 0;
        heldOffsets = null;
        lastPlayerPositions.Clear();
        ApplyAuthority();
        sync.Publish();
    }
    /// <summary>
    /// Kabin cesedi bırakıyor: gövde yeniden fiziğe dönüyor.
    ///
    /// Kabin sıfırlanınca (tur bitişi gibi) `corpseId` temizleniyordu ama ceset
    /// `stationNetId`'yi taşımaya devam ediyordu; `IsHeld` sonsuza kadar doğru
    /// kalıyor, gövde kinematik ve collider'ları kapalı donuyordu. Alınamayan,
    /// itilemeyen, düşmeyen bir beden kalıyordu ortada.
    /// </summary>
    [Server] public void ServerReleaseFromStation()
    {
        if (stationNetId == 0) return;
        stationNetId = 0;
        heldOffsets = null;
        ApplyAuthority();
        sync.Publish();
    }
    [Server] public bool ServerDeposit(RoundParticipant carrier, RevivalStation station)
    {
        if (carrier == null || carrier.netId != carrierNetId || station == null) return false;
        carrierNetId = 0;
        stationNetId = station.netId;
        ApplyAuthority();
        MoveHeld(station.BodyAnchor.position, station.BodyAnchor.rotation);
        sync.Publish();
        return true;
    }
    public static bool ClearReach(Vector3 from, Vector3 to) => !Physics.Linecast(from, to,
        LayerMask.GetMask("Harita"), QueryTriggerInteraction.Ignore);
    [Server] private void ShoveFromPlayers()
    {
        if (pushStrength <= 0) return;
        int count = Physics.OverlapSphereNonAlloc(Position, 2.5f, nearbyPlayers, playerMask, QueryTriggerInteraction.Ignore);
        if (count == 0) { lastPlayerPositions.Clear(); return; }
        for (int i = 0; i < count; i++)
        {
            Collider player = nearbyPlayers[i];
            Vector3 current = player.transform.position;
            bool known = lastPlayerPositions.TryGetValue(player.transform, out Vector3 previous);
            lastPlayerPositions[player.transform] = current;
            if (!known) continue;
            Vector3 velocity = Vector3.ProjectOnPlane(current - previous, Vector3.up) / Time.fixedDeltaTime;
            velocity = Vector3.ClampMagnitude(velocity, 12f);
            if (velocity.magnitude < 0.5f) continue;
            Bounds reach = player.bounds; reach.Expand(pushReach * 2f);
            foreach (var part in ragdoll)
                if (reach.Intersects(part.Collider.bounds))
                { part.Body.WakeUp(); part.Body.AddForce(velocity * pushStrength * part.Body.mass, ForceMode.Impulse); }
        }
    }
}
