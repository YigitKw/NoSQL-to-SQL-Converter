# NoSQL (JSON) -> SQL Dönüştürücü

**[🇹🇷 Türkçe](README.tr.md) | [🇬🇧 English](README.md)**

Bu uygulama, herhangi bir JSON belgesini çözümleyip ilişkisel bir SQLite veritabanına aktaran Windows Forms masaüstü uygulamasıdır. İç içe nesneleri düzleştirerek ana tabloya ekler, dizi (liste) yapılarını ise 1:N ilişkili alt tablolara ayırarak birbirine bağlar.

---

## Kurulum ve Çalıştırma

Bilgisayarınızda **.NET 9.0** yüklü olmalıdır.

1. **Visual Studio ile:**
   - `240201025/Proje3.sln` dosyasını Visual Studio ile açın.
   - Üst bardaki **Başlat (F5)** butonuna tıklayarak çalıştırın.

2. **Komut Satırı ile:**
   ```bash
   cd 240201025
   dotnet run
   ```

---

## Kullanım Adımları

Uygulama açıldığında üstte butonlar, solda JSON ağacı ve SQL log alanı, sağda ise tablolar ve sorgu paneli yer alır.

1. **JSON Seç:** Üst paneldeki `JSON Dosyası Seç` butonuna tıklayıp bir `.json` dosyası seçin.
   - Sol üstteki `JSON Yapısı` ağacında dosyanın içeriği listelenir.
   - `Dönüştür` butonu aktifleşir.
2. **Dönüştür:** `Dönüştür` butonuna basın.
   - Tablolar sırayla açılır (`CREATE TABLE`), veriler eklenir (`INSERT INTO`) ve sağ tarafta her tablo için birer sekme açılır.
   - Sol alttaki `SQL LOG` alanından arka planda çalışan sorguları takip edebilirsiniz.
3. **Tabloları ve Verileri İnceleme:**
   - Sağdaki sekmelere tıklayarak eklenen satırları görebilirsiniz.
   - Sağ üstteki `SQL SORGUSU` kutusuna sorgu yazıp `Çalıştır` diyerek sonuçları alttaki tabloda görebilirsiniz.
4. **Sıfırlama:**
   - Yeni bir dosya denemek veya veritabanını temizlemek isterseniz `Sıfırla` butonuna basabilirsiniz. Bu işlem tablolardaki verileri ve tabloları siler (`DropAllTables`), ekranı ilk haline getirir.

---

## Parametreler ve Özelleştirme Rehberi

Projede ihtiyaca göre değiştirilebilecek veya bilinçli olarak sabit bırakılmış ayarlar:

### 1. Veritabanı Dosya Yolu ve Kalıcılık (`_databaseFilePath` — `Form1.cs`)
* **Varsayılan Değer:** Uygulamanın çalıştığı dizinde `proje3.db`.
* **Kalıcılık Davranışı:** Uygulama her açılışında önceki oturumdan kalan `proje3.db` dosyasını korur ve mevcut tabloları otomatik olarak arayüze yükler. Dosya yoksa sıfırdan oluşturulur.
* **Veritabanını Sıfırlama:** Tabloları temizlemek ve veritabanını sıfırdan başlatmak için üst paneldeki `Sıfırla` butonu kullanılır.
* **Dosya Yolunu Değiştirme:** `InitializeNewDatabase` metodundaki `_databaseFilePath` yolunu dilediğiniz bir dizine yönlendirebilirsiniz.

### 2. INSERT Log Sınırı (`_persistedRecordLogCount` — `Form1.cs`)
* **Varsayılan Değer:** İlk `100` kayıt loglanır, sonrakiler gizlenir.
* **Neden Bu Değer Seçildi?:** Çok satırlı JSON dosyalarında her INSERT işlemini `RichTextBox` bileşenine yazdırmak arayüzü yavaşlatır.
* **Nasıl Değiştirilir?:** `InsertRecordRecursive` fonksiyonundaki `_persistedRecordLogCount <= 100` koşulundaki sayıyı artırarak daha fazla sorguyu ekranda görebilirsiniz.

### 3. SQLite Performans Ayarları (`DatabaseManager.cs`)
* **Kullanılan Ayarlar:** `PRAGMA journal_mode = WAL;` ve `PRAGMA synchronous = NORMAL;`
* **Neden Değiştirilmemeli?:** SQLite varsayılan kipinde her yazmada diski kilitler. WAL (Write-Ahead Logging) modu toplu kayıt eklerken disk yazma darboğazını önler. Dosya ağ sürücüsünde (SMB/ağ paylaşımı) değilse bu ayarların kalması en iyisidir.

### 4. Sistem Anahtarları (`_id` ve `{ParentTable}__id`)
* **Neden Değiştirilmemeli?:** JSON verinizin içinde doğal olarak `id`, `ID` gibi alanlar bulunabilir. Veritabanının ürettiği anahtarla JSON'daki alanların çakışmaması için sistem anahtarı `_id`, yabancı anahtar ise `__id` (çift alt çizgi) olarak ayrılmıştır.

### 5. Grid Önizleme Sınırı (`LIMIT 500` — `Form1.cs`)
* **Varsayılan Değer:** Arayüzdeki tablo sekmelerinde ilk `500` satır önizlenir. Toplam satır sayısı SQL log alanında raporlanır (`SELECT COUNT(*)`).
* **Neden Bu Sınır Var?:** On binlerce satır içeren büyük tablolarda tüm kayıtların aynı anda WinForms `DataGridView` bileşenine yüklenmesi yüksek bellek tüketimine ve arayüz donmalarına yol açar. SQLite tablosunda verilerin tamamı eksiksiz saklanır.
* **Tüm Verilere Erişme:** İlgili tablonun tüm satırlarına veya belirli aralıklarına sağ üstteki SQL sorgu editöründen sorgu çalıştırarak (örneğin `SELECT * FROM tablo WHERE ...`) erişebilirsiniz.

---

## Teknik İnceleme ve Karmaşıklık Analizi

### Mimari Akış ve Kod Eşleşmesi

| Aşama | Sorumlu Sınıf & Metot | Yapılan İşlem |
|---|---|---|
| **Pass 1 (Şema Keşfi)** | `JsonParser.WalkForSchemas()` | Veri yazmadan tüm JSON dolaşılır. `EnsureColumn()` ve `PromoteType()` ile kolonlar ve veri tipleri belirlenir. |
| **Pass 2 (Kayıt Üretimi)** | `JsonParser.ProcessObject()` | Kesinleşen şemalar üzerinden `FlatRecord` nesneleri üretilir. |
| **Düzleştirme (Flattening)** | `JsonParser.FlattenObject()` | 1:1 iç içe nesneler ana tabloya `ebeveyn_cocuk` sütunu olarak bağlanır (gereksiz JOIN maliyetini önlemek için). |
| **Normalizasyon (1NF & 2NF)** | `JsonParser.ProcessArray()` | 1:N diziler ana tablodan ayrılarak alt tabloya taşınır. Her tabloya otomatik `_id` ve `parent__id` bağlanır. |
| **Önbellekli SQL Yazımı** | `DatabaseManager.InsertRecord()` | `_preparedInsertCommands` sözlüğü sayesinde her tablo için tek `SqliteCommand` derlenir, satırlar sadece parametreleri güncellenerek eklenir. |

### Zaman Karmaşıklığı (Time Complexity)

JSON belgesindeki toplam düğüm sayısı **N**, oluşturulan ilişkisel satır sayısı **M** olsun:

1. **Şema Keşfi (Pass 1):** Ağaçtaki her JSON düğümü bir kez ziyaret edilir -> O(N).
2. **Kayıt Ayıklama (Pass 2):** Her düğüm değer üretimi için ikinci kez ziyaret edilir -> O(N).
3. **Veritabanı Yazma:** Tek bir `Transaction` bloğu ve önbelleklenmiş `Prepared Statement` kullanımı sayesinde her satır ekleme işlemi amortize O(1) sürede gerçekleşir. Toplam yazma süresi -> O(M).

> **Toplam Zaman Karmaşıklığı:** O(N + M) = O(N) (Doğrusal). Toplam süre dosyadaki eleman sayısıyla doğru orantılıdır.

### Bellek Karmaşıklığı (Space Complexity)

* **Bellek Tüketimi:** JSON ağacının tamamı `JToken.Parse` ile bellekte tutulduğu için bellek karmaşıklığı O(N)'dir.
* **Şema Sözlüğü:** Şema meta verileri tablo ve sütun sayısıyla orantılıdır O(T * C); dosya boyutuna kıyasla oldukça küçüktür.
* **Pratik Durum:** 15-20 MB boyutundaki bir JSON dosyası bellekte ortalama 60-90 MB yer tutar ve bellek sorunu yaşatmaz.

---

## Test Verileri (`240201025/Samples/`)

Projeyi test etmek için `240201025/Samples` klasöründe 4 farklı JSON dosyası bulunuyor:

* `simple.json`: Tek seviyeli, temel alanları içeren basit yapı.
* `nested.json`: İç içe geçmiş nesneler ve ürün dizisi içeren sipariş modeli.
* `complex.json`: Fakülte, öğretim üyeleri, dersler ve öğrenciler gibi çok katmanlı yapı.
* `data.json`: ~14 MB boyutunda, 8.000'den fazla mobil cihaz kaydı ve derinlemesine iç içe geçmiş teknik özellik (`specs`) nesneleri barındıran gerçek dünya büyük veri seti (toplu ekleme performansı ve düzleştirme testi için).

---

## Bağımlılıklar

* [.NET 9.0](https://dotnet.microsoft.com/)
* [Newtonsoft.Json (13.0.4)](https://www.nuget.org/packages/Newtonsoft.Json/) — JSON ayrıştırma
* [Microsoft.Data.Sqlite (10.0.8)](https://www.nuget.org/packages/Microsoft.Data.Sqlite/) — SQLite veritabanı kütüphanesi
