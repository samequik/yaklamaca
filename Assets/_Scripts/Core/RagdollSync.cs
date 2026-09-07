using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Sunucudaki kemik pozları: taşıma ve geç katılmada da aynı beden.</summary>
public class RagdollSync : NetworkBehaviour
{
    [SerializeField] private float sendRate = 12f;
    [SerializeField] private float smoothing = 18f;
    [SyncVar(hook = nameof(OnPoseChanged))] private byte[] pose;
    private List<RagdollFactory.Part> parts;
    private Vector3[] positions;
    private Quaternion[] rotations;
    private bool hasTarget, sentAsleep;
    private float nextSend;
    public bool Continuous { get; set; }

    public void Bind(List<RagdollFactory.Part> value)
    {
        parts = value;
        positions = new Vector3[value.Count];
        rotations = new Quaternion[value.Count];
        if (!isServer) Unpack(pose);
    }
    private void FixedUpdate()
    {
        if (!isServer || parts == null || parts.Count == 0 || Time.time < nextSend) return;
        bool asleep = !Continuous;
        foreach (var part in parts)
            if (!part.Body.IsSleeping()) asleep = false;
        if (asleep && sentAsleep) return;
        sentAsleep = asleep;
        nextSend = Time.time + 1f / Mathf.Max(1f, sendRate);
        Publish();
    }
    [Server]
    public void Publish()
    {
        if (parts == null || parts.Count == 0) return;
        using (NetworkWriterPooled writer = NetworkWriterPool.Get())
        {
            // Dünya pozları, yerel ölüm animasyonunun hangi karede olduğundan bağımsız.
            foreach (var part in parts)
            {
                writer.WriteVector3(part.Bone.position);
                writer.WriteUInt(Compression.CompressQuaternion(part.Bone.rotation));
            }
            pose = writer.ToArray();
        }
    }
    private void Update()
    {
        if (isServer || !hasTarget || parts == null) return;
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        for (int i = 0; i < parts.Count; i++)
            parts[i].Bone.SetPositionAndRotation(Vector3.Lerp(parts[i].Bone.position, positions[i], t),
                Quaternion.Slerp(parts[i].Bone.rotation, rotations[i], t));
    }
    private void OnPoseChanged(byte[] previous, byte[] current) => Unpack(current);
    private void Unpack(byte[] data)
    {
        if (parts == null || data == null || data.Length != parts.Count * 16) return;
        using (NetworkReaderPooled reader = NetworkReaderPool.Get(data))
            for (int i = 0; i < parts.Count; i++)
            {
                positions[i] = reader.ReadVector3();
                rotations[i] = Compression.DecompressQuaternion(reader.ReadUInt());
            }
        if (!hasTarget)
            for (int i = 0; i < parts.Count; i++)
                parts[i].Bone.SetPositionAndRotation(positions[i], rotations[i]);
        hasTarget = true;
    }
}
