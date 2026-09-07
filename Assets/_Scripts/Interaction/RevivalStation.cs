using Mirror;
using UnityEngine;

/// <summary>Ceset kabulü ve diriltme kararı sunucuda; normal hedef terminallerinden bağımsız.</summary>
public class RevivalStation : NetworkBehaviour, IInteractable
{
    [SerializeField] private Transform bodyAnchor;
    [SerializeField] private Transform revivePoint;
    [SerializeField] private Renderer indicator;
    [SerializeField] private AudioClip workingClip;
    [SerializeField] private AudioClip warningClip;
    [SerializeField] private float duration = 15f;
    [SerializeField] private float useDistance = 2.8f;
    [SyncVar] private uint corpseId;
    [SyncVar] private uint operatorId;
    [SyncVar] private float elapsed;
    [SyncVar] private bool locked;
    [SyncVar] private byte prompt;
    [SyncVar] private int promptToken;
    [SyncVar] private double deadline;
    [SyncVar] private int unlockCode;
    [SyncVar] private int unlockEntered;
    [SyncVar] private double alarmUntil;
    private int checksPassed;
    private int answeredToken = -1;
    private float readyAt;
    private bool localFocused;
    private PlayerController focusedController;
    private AudioSource audioSource;
    private MaterialPropertyBlock block;
    public static RevivalStation ActiveLocal { get; private set; }
    public Transform BodyAnchor => bodyAnchor != null ? bodyAnchor : transform;
    public float Progress => Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.1f));
    public float Remaining => Mathf.Max(0, duration - elapsed);
    public bool Locked => locked;
    public byte Prompt => prompt;
    public double Deadline => deadline;
    public int UnlockCode => unlockCode;
    public int UnlockEntered => unlockEntered;
    public bool IsLocalOperator => NetworkClient.localPlayer != null && operatorId == NetworkClient.localPlayer.netId;
    public Corpse Body => FindCorpse(corpseId);

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false; audioSource.loop = true;
        audioSource.spatialBlend = 1; audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 2; audioSource.maxDistance = 18;
    }
    public override void OnStartClient() => RevivalScreen.Ensure();
    public static Corpse FindCorpse(uint id)
    {
        NetworkIdentity identity;
        if (NetworkServer.active && NetworkServer.spawned.TryGetValue(id, out identity)) return identity.GetComponent<Corpse>();
        return NetworkClient.spawned.TryGetValue(id, out identity) ? identity.GetComponent<Corpse>() : null;
    }
    private void Update()
    {
        if (isServer) ServerTick();
        if (isClient) TickLocal();
        Color color = locked ? new Color(1, 0.15f, 0.05f) : operatorId != 0 ? Color.cyan : new Color(0.2f, 0.7f, 0.55f);
        if (indicator != null)
        {
            block.SetColor("_Color", color); block.SetColor("_EmissionColor", color * 0.5f);
            indicator.SetPropertyBlock(block);
        }
        AudioClip wanted = NetworkTime.time < alarmUntil ? warningClip : operatorId != 0 && !locked ? workingClip : null;
        if (audioSource.clip != wanted)
        {
            audioSource.Stop(); audioSource.clip = wanted;
            audioSource.volume = locked ? 0.7f : 0.4f;
            if (wanted != null) audioSource.Play();
        }
    }
    private void OnDisable() => ReleaseLocal();
    private void ReleaseLocal()
    {
        if (!localFocused) return;
        localFocused = false;
        if (focusedController != null) focusedController.EndFocus();
        focusedController = null;
        PlayerInteractor.InputCaptured = false;
        if (ActiveLocal == this) ActiveLocal = null;
    }
    private void TickLocal()
    {
        var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<RoundParticipant>() : null;
        bool active = IsLocalOperator && Corpse.LivingRunner(local);
        if (!active) { ReleaseLocal(); return; }
        if (!localFocused)
        {
            focusedController = local.GetComponent<PlayerController>();
            if (focusedController != null) focusedController.BeginFocus(25f, 12f);
            localFocused = true; ActiveLocal = this; PlayerInteractor.InputCaptured = true;
            answeredToken = -1;
            return; // Kullanmayı başlatan E bu karede bırakma sayılmasın.
        }
        if (MenuController.Instance != null && MenuController.Instance.IsOpen) return;
        if (KeyBindings.Pressed(GameAction.Interact)) { CmdRelease(); return; }
        if ((!locked && prompt == 0) || answeredToken == promptToken) return;
        for (byte direction = 1; direction <= 4; direction++)
            if (KeyBindings.Pressed(DirectionAction(direction)))
            {
                answeredToken = promptToken;
                CmdAnswer(direction, promptToken);
                break;
            }
    }
    public static GameAction DirectionAction(int direction) => direction == 1 ? GameAction.Forward
        : direction == 2 ? GameAction.Back : direction == 3 ? GameAction.Left : GameAction.Right;
    public string GetPrompt()
    {
        var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<RoundParticipant>() : null;
        if (!Corpse.LivingRunner(local)) return null;
        if (operatorId != 0) return "Diriltme terminali kullanımda";

        bool carrying = Corpse.CarriedBy(local) != null;

        if (corpseId == 0)
            return carrying ? "Cesedi kabine yerleştir" : "Diriltme kabini — bir ceset getir";

        // Elinde ceset varken terminali çalıştıramıyorsun (CmdUse reddediyor).
        // Yazı bunu söylemezse oyuncu E'ye basıp hiçbir şey olmadığını görüyor
        // ve kabini bozuk sanıyor.
        if (carrying) return "Kabin dolu — taşıdığın cesedi önce bırak";

        return locked ? "Diriltme terminalinin kilidini aç" : "Diriltmeyi başlat — 15 saniye";
    }
    public void Interact(GameObject user) => CmdUse();
    private RoundParticipant Validate(NetworkConnectionToClient sender)
    {
        var player = sender?.identity != null ? sender.identity.GetComponent<RoundParticipant>() : null;
        if (!Corpse.LivingRunner(player) || Vector3.Distance(player.transform.position, transform.position) > useDistance) return null;
        if (!Corpse.ClearReach(player.transform.position + Vector3.up * 0.8f, transform.position + Vector3.up * 0.2f)) return null;
        return player;
    }
    [Command(requiresAuthority = false)]
    private void CmdUse(NetworkConnectionToClient sender = null)
    {
        var player = Validate(sender);
        if (player == null || operatorId != 0) return;
        if (player.GetComponent<PlayerController>()?.IsFocused == true) return;
        Corpse carried = Corpse.CarriedBy(player);
        if (corpseId == 0)
        {
            if (carried == null || !carried.ServerDeposit(player, this)) return;
            corpseId = carried.netId;
            ResetProgress();
            return;
        }
        if (carried != null || Body == null) return;
        var victim = Corpse.Resolve(Body.VictimNetId);
        if (victim == null || victim.IsAlive) return;
        operatorId = player.netId; readyAt = Time.time + 0.6f;
        promptToken++;
    }
    [Command(requiresAuthority = false)]
    private void CmdRelease(NetworkConnectionToClient sender = null)
    {
        if (sender?.identity != null && sender.identity.netId == operatorId) ReleaseOperator();
    }
    [Command(requiresAuthority = false)]
    private void CmdAnswer(byte answer, int token, NetworkConnectionToClient sender = null)
    {
        var player = Validate(sender);
        if (player == null || player.netId != operatorId || token != promptToken || answer < 1 || answer > 4) return;
        if (locked)
        {
            int expected = ((unlockCode >> (unlockEntered * 2)) & 3) + 1;
            unlockEntered = answer == expected ? unlockEntered + 1 : 0;
            promptToken++;
            if (unlockEntered == 4)
            { locked = false; alarmUntil = 0; unlockEntered = 0; ResetProgress(); readyAt = Time.time + 0.6f; }
            return;
        }
        if (prompt == 0) return;
        if (answer != prompt || NetworkTime.time > deadline + 0.3) { Fail(); return; }
        prompt = 0; checksPassed++; promptToken++;
    }
    [Server] private void ServerTick()
    {
        if (RoundManager.Instance == null || RoundManager.Instance.Phase != RoundPhase.Playing)
        {
            if (corpseId != 0 || operatorId != 0 || locked) ResetStation();
            return;
        }
        if (corpseId != 0)
        {
            Corpse body = Body;
            var victim = body != null ? Corpse.Resolve(body.VictimNetId) : null;
            if (body == null || victim == null || victim.IsAlive)
            {
                if (body != null) NetworkServer.Destroy(body.gameObject);
                ResetStation(); return;
            }
        }
        if (operatorId == 0) return;
        var user = Corpse.Resolve(operatorId);
        if (!Corpse.LivingRunner(user) || Vector3.Distance(user.transform.position, transform.position) > useDistance)
        { ReleaseOperator(); return; }
        if (locked || Time.time < readyAt) return;
        if (prompt != 0)
        { if (NetworkTime.time > deadline + 0.3) Fail(); return; }
        if (checksPassed < 3 && elapsed >= 3f + checksPassed * 4f)
        { prompt = (byte)Random.Range(1, 5); deadline = NetworkTime.time + 1.8; promptToken++; return; }
        elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
        if (elapsed < duration || checksPassed < 3) return;
        // Sayaç güncellemesi ve cesedin tüketilmesi tek sunucu işlemi.
        if (RoundManager.Instance.ServerRevive(Body, revivePoint != null ? revivePoint : BodyAnchor))
            ResetStation();
    }
    [Server] private void Fail()
    {
        locked = true; ResetProgress(); unlockEntered = 0; unlockCode = 0;
        for (int i = 0; i < 4; i++) unlockCode |= Random.Range(0, 4) << (i * 2);
        alarmUntil = NetworkTime.time + 20; promptToken++;
    }
    [Server] private void ResetProgress() { elapsed = 0; prompt = 0; checksPassed = 0; promptToken++; }
    [Server] private void ReleaseOperator() { operatorId = 0; ResetProgress(); unlockEntered = 0; }
    [Server] private void ResetStation()
    {
        // Ceset varsa ÖNCE serbest bırakılıyor: yalnızca corpseId'yi silmek
        // gövdeyi kabinde donmuş hâlde bırakıyordu (bkz. ServerReleaseFromStation).
        // Diriltme yolunda ceset zaten yok edilmiş oluyor, o zaman Body null.
        Corpse body = Body;
        if (body != null) body.ServerReleaseFromStation();
        ReleaseOperator(); corpseId = 0; locked = false; alarmUntil = 0;
    }
}
