using UnityEngine;

/// <summary>
/// Menü sahnesinin kamerası: çizimi boyunca sisi kapatıyor.
///
/// `RenderSettings.fog` **global** — haritanın 0.045 yoğunluğu menü sahnesine
/// de biniyor ve karakterleri soluklaştırıyordu. Built-in RP'de kamera başına
/// sis ayarı yok, o yüzden çizimden hemen önce kapatıp hemen sonra geri açmak
/// tek yol.
///
/// Geri çağrılar **kameranın kendi objesinde** olmak zorunda; bu yüzden
/// `MenuStage` değil ayrı bir bileşen. Aynı sebeple `MonsterAura` ve
/// `Terminal`'de de ışıklar kendi objelerinde duruyor.
///
/// Ayrıca `NetworkPlayerSetup.DisableOtherCameras` bu bileşeni görüp kamerayı
/// ödünç almıyor: yerel oyuncu doğduğunda sahnedeki bütün kameraları kapatıyor
/// (bölüm 13) ve menü sahnesi lobide de çalışmak zorunda.
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class MenuStageCamera : MonoBehaviour
{
    private bool fogWasOn;

    private void OnPreRender()
    {
        fogWasOn = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    private void OnPostRender() => RenderSettings.fog = fogWasOn;
}
