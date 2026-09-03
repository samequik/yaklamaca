using UnityEngine;

/// <summary>
/// Oyuncunun nişan alıp E ile kullanabildiği şeyler. Düğmeler, kapı kolları,
/// ileride eşyalar. Sadece "bakılıp kullanılabilir olmayı" tanımlar; ne olacağı
/// uygulayan sınıfın işi.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Ekranda gösterilecek metin, örn. "Kapıyı aç".
    /// Boş veya null döndürmek "şu an kullanılamaz" demektir; oyuncu nişan alsa
    /// bile yazı çıkmaz ve E işlemez. Kilitli kapı, biten pil vb. için.
    /// </summary>
    string GetPrompt();

    /// <summary>Kullanan oyuncu — ileride "kim açtı" bilgisi gerekince lazım olacak.</summary>
    void Interact(GameObject user);
}
