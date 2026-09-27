using UnityEngine;

/// <summary>
/// Bir doğum noktasının KİME ait olduğunu söyleyen işaret.
///
/// ### Neden ada değil bileşene bakıyoruz
///
/// Nokta "Dogum_Canavar_2" diye adlandırılıyor ve rolü addan okumak cazip
/// duruyor. Bu proje o tuzağa iki kez düştü: `GameObject.Find("Duvar_3_0")`
/// iki ayrı haritada aynı adı bulup hangisini döndüreceğini garanti
/// etmiyordu (bölüm 0.1) ve sopanın el kemiği de bilerek adıyla değil
/// ROLÜYLE (`HumanBodyBones.RightHand`) çözülüyor (bölüm 14). Ad bir
/// etiket, rol bir veri — biri yeniden adlandırılınca öbürü bozulmamalı.
///
/// ### Mirror'ın kendi işaretinin yerine GEÇMİYOR
///
/// Aynı objede `NetworkStartPosition` de duruyor ve durması gerekiyor:
/// oyuncu LOBİDE Mirror'ın seçtiği noktada doğuyor, rol ise ancak tur
/// başlarken belli oluyor (bölüm 11.1). Yani Mirror lobiyi, bu bileşen turu
/// yönetiyor — ikisi farklı anların sorusunu cevaplıyor.
///
/// Alan `Yakalamaca > Doğum Noktalarını Kur (iki harita)` tarafından
/// yazılıyor; elle de ayarlanabilir.
/// </summary>
[DisallowMultipleComponent]
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Bu noktada kim doğuyor. Kaçan noktaları ANA haritaya, canavar " +
        "noktaları GÜNEY kanadına dağıtılıyor — tur başında ikisi arasında " +
        "haritanın boyu kadar mesafe kalsın diye.")]
    [SerializeField] private RoundRole role = RoundRole.Runner;

    public RoundRole Role => role;

#if UNITY_EDITOR
    /// <summary>
    /// Kurulum aracının rolü yazdığı yer. `[SerializeField] private` alanı
    /// dışarıdan açmak yerine tek bir kapı bırakılıyor — bu projenin
    /// "prefab/sahne değerlerini ARAÇ yazar" kuralıyla aynı desen.
    /// </summary>
    public void EditorAssign(RoundRole value) => role = value;
#endif
}
