# NoSQL (JSON) -> SQL Dönüştürücü

**[🇹🇷 Türkçe](README.tr.md) | [🇬🇧 English](README.md)**

Hiyerarşik JSON belgelerini çözümleyip ilişkisel bir SQLite veritabanına aktaran Windows Forms masaüstü uygulamasıdır. Sistem, 1:1 iç içe nesneleri üst tablo sütunlarına düzleştirirken (flattening), 1:N dizi yapılarını yabancı anahtar (foreign key) kısıtlarıyla bağlanan bağımsız alt tablolara ayrıştırır.

![Uygulama Arayüzü](docs/screenshot.tr.png)

---

## Kurulum ve Çalıştırma

Uygulamanın derlenmesi ve çalıştırılması için **.NET 9.0 SDK** veya çalışma zamanı gereklidir.

1. **Visual Studio ile:**
   - `240201025/Proje3.sln` çözüm dosyasını Visual Studio ile açın.
   - **Başlat (F5)** düğmesiyle projeyi derleyip çalıştırın.

2. **Komut Satırı ile:**
   ```bash
   cd 240201025
   dotnet run
   ```

---

## Kullanım Yönergesi

Ana arayüz; üst işlem paneli, solda JSON hiyerarşi ağacı, sol altta SQL işlem günlüğü, sağda ise ilişkisel tablo sekmeleri ile tümleşik SQL sorgu konsolundan oluşur.

1. **JSON Belgesi Seçimi:** Üst paneldeki `JSON Dosyası Seç` düğmesine tıklayarak bir `.json` dosyası seçin.
   - Sol paneldeki ağaç bileşeni, belgenin yapısal hiyerarşisini listeler.
   - 50 elemandan fazla öğe barındıran dizilerde, kullanıcı arayüzü iş parçacığının yanıt verebilirliğini korumak amacıyla ilk 50 düğüm listelenir ve kalan kayıt adedi özet etiketi olarak eklenir.
   - Belge okunduğunda `Dönüştür` düğmesi etkinleşir.
2. **Dönüştürme İşlemi:** `Dönüştür` düğmesine tıklayın.
   - Sistem şema keşfini tamamlar, dinamik `CREATE TABLE` komutlarını yürütür ve açık bir işlem (transaction) bloğu içinde parametreli `INSERT INTO` sorgularıyla verileri aktarır.
   - Yürütülen işlemler `SQL LOG` alanından gerçek zamanlı izlenebilir.
3. **Veri İnceleme ve Sorgulama:**
   - Sağdaki sekmeler, oluşturulan tabloları salt okunur veri ızgaralarında görüntüler.
   - `SQL SORGUSU` kutusu; `SELECT`, ortak tablo ifadeleri (`WITH`), `PRAGMA` ve `EXPLAIN` sorgularını destekler. Çalıştırılan sorgu sonuçları `Sorgu Sonucu` sekmesine yönlendirilir.
4. **Durum Sıfırlama:**
   - `Sıfırla` düğmesi, `DropAllTables` yordamıyla tüm kullanıcı tablolarını kaldırır ve yerel arabellekleri temizler.
5. **Arayüz Dili Değiştirme:**
   - Üst paneldeki `🌐 EN` / `🌐 TR` düğmesine tıklayarak tüm arayüz denetimlerini, panel başlıklarını ve bildirimleri uygulamayı yeniden başlatmadan Türkçe ve İngilizce arasında dönüştürebilirsiniz.

---

## Mimari Parametreler ve Sistem Davranışları

Sistem mimarisinde tanımlanan temel parametreler ve gerekçeleri:

### 1. Veritabanı Konumu ve Kalıcılık (`_databaseFilePath` in `Form1.cs`)
* **Varsayılan Değer:** Uygulama çalışma dizininde `proje3.db`.
* **Kalıcılık Politikası:** Uygulama, oturumlar arasında mevcut `proje3.db` dosyasını korur ve açılışta var olan tabloları yükler. Dosya mevcut değilse yeni bir veritabanı başlatılır.
* **Şema Sıfırlama:** `Sıfırla` denetimi, tabloları `DROP TABLE` komutlarıyla siler.
* **Yol Değişikliği:** Dosya yolu `InitializeNewDatabase` yordamı üzerinden yapılandırılabilir.

### 2. INSERT Günlük Sınırı (`_persistedRecordLogCount` in `Form1.cs`)
* **Varsayılan Değer:** İlk 100 ekleme işlemi günlüğe yazılır; sonrakiler gizlenir.
* **Gerekçe:** WinForms `RichTextBox` bileşenine yüksek frekansta metin eklenmesi kullanıcı arayüzü iş parçacığında mesaj kuyruğu darboğazına yol açar.
* **Yapılandırma:** Eşik değeri `InsertRecordRecursive` içindeki `_persistedRecordLogCount <= 100` koşulundan değiştirilebilir.

### 3. TreeView Düğüm Sınırlandırması (`previewLimit` in `Form1.cs`)
* **Varsayılan Değer:** Dizi seviyesi başına 50 eleman.
* **Gerekçe:** `data.json` gibi 8.794 nesne içeren büyük veri setleri eksiksiz ayrıştırıldığında 300.000'den fazla `TreeNode` örneği üretir. Sınırlandırma, hiyerarşik görünürlüğü korurken yerel Windows tanıtıcı (handle) tahsisini dengeler.

### 4. Hazırlanmış Komut Önbelleği Yönetimi (`DatabaseManager.cs`)
* **İşleyiş:** INSERT komutları `_preparedInsertCommands` sözlüğünde önbelleğe alınır.
* **Önbellek Temizleme:** Önbellek, `ClearPreparedCommands()` ile her dönüştürme döngüsü öncesinde temizlenir. Bu sayede veritabanı sıfırlanmadan art arda farklı şemaya sahip dosyalar işlendiğinde sütun atlanması engellenir.

### 5. Kültürden Bağımsız Tanımlayıcı Normalizasyonu (`JsonParser.cs`)
* **İşleyiş:** SQL tanımlayıcıları temizlenir ve `ToLowerInvariant()` ile küçük harfe dönüştürülür.
* **Gerekçe:** Türkçe yerel ayarlarında standart `ToLower()` çağrısı noktasız 'ı' karakteri üretir. Bu durum SQLite'ın ASCII tabanlı büyük/küçük harf duyarsız sütun çözümleme mekanizmasında eşleşme hatalarını önler.

### 6. SQLite Eşzamanlılık Ayarları (`DatabaseManager.cs`)
* **Yapılandırma:** `PRAGMA journal_mode = WAL;` ve `PRAGMA synchronous = NORMAL;`
* **Gerekçe:** SQLite varsayılan günlük kipinde yazma işlemleri sırasında dosya tanımlayıcısını kilitler. WAL (Write-Ahead Logging) kipi, okuma ve yazma işlemlerini ayrıştırarak toplu eklemeleri hızlandırır.

### 7. Birincil ve Yabancı Anahtar Tanımlayıcıları
* **Tasarım:** Birincil anahtarlar `_id` (otomatik artan tamsayı), yabancı anahtarlar `{ParentTable}__id` olarak adlandırılır.
* **Gerekçe:** Sistem anahtarlarının çift alt çizgiyle ayrılması, girdi JSON verisindeki `id` veya `ID` gibi doğal alanlarla ad çakışmasını engeller.

### 8. Izgara Önizleme Sınırı (`LIMIT 500` in `Form1.cs`)
* **Varsayılan Değer:** Sekmelerdeki ızgara görünümleri her tablo için ilk 500 kaydı önizler.
* **Gerekçe:** Çok yüksek hacimli veri setlerinin doğrudan `DataGridView` denetimine bağlanması yüksek GDI bellek tüketimine sebep olur. Toplam satır sayısı `SELECT COUNT(*)` ile günlüğe raporlanır ve verilerin tamamına SQL konsolundan erişilebilir.

### 9. Çalışma Zamanı İki Dilli Arayüz Desteği (TR / EN)
* **İşleyiş:** `ToggleLanguage()` yordamı üzerinden yönetilen tek tıkla arayüz dili geçişi.
* **Tasarım:** Değerlendirme kriterlerine uyum amacıyla açılışta Türkçe varsayılan olarak korunur; kullanıcı eylemiyle tüm etkileşimli bileşenler, sistem uyarıları ve panel etiketleri çalışma zamanında anında İngilizceye çevrilir.

---

## Teknik Aşamalar ve Karmaşıklık Analizi

### Bileşen Mimarisi

| İşlem Aşaması | Sorumlu Sınıf ve Metot | Yapılan İşlem |
|---|---|---|
| **Geçiş 1: Şema Keşfi** | `JsonParser.WalkForSchemas()` | Veri tabanına yazma yapmadan JSON düğüm grafiğini dolaşır. `EnsureColumn()` ve `PromoteType()` ile tablo ilişkilerini, sütunları ve en geniş veri tiplerini belirler. |
| **Geçiş 2: Kayıt Üretimi** | `JsonParser.ProcessObject()` | Kesinleşen şema kayıt defterine uygun `FlatRecord` varlıkları üretir. |
| **Nesne Düzleştirme** | `JsonParser.FlattenObject()` | 1:1 iç içe nesne yapılarını üst tablodaki `ebeveyn_cocuk` sütunlarına düzleştirerek gereksiz ilişkisel birleştirme (JOIN) maliyetlerini ortadan kaldırır. |
| **Dizi Normalizasyonu (1NF & 2NF)** | `JsonParser.ProcessArray()` | 1:N dizi koleksiyonlarını alt tablolara ayırır, otomatik artan `_id` birincil anahtarı ve üst tablo yabancı anahtarını (`parent__id`) atar. |
| **Önbellekli Komut Yürütme** | `DatabaseManager.InsertRecord()` | Kayıt niteliklerini, açık işlem kapsamında derlenmiş `SqliteCommand` şablonlarına bağlayarak çalıştırır. |

### Zaman Karmaşıklığı (Time Complexity)

Girdi belgesindeki toplam JSON belirteci (token) sayısı **N**, üretilen ilişkisel kayıt sayısı **M** olsun:

1. **Şema Keşfi (Geçiş 1):** Her JSON belirteci bir kez ziyaret edilir: O(N).
2. **Kayıt Ayıklama (Geçiş 2):** Belirteç değerleri tek bir dolaşımda ayıklanır: O(N).
3. **Veritabanı Kalıcılığı:** Tek bir işlem bloğu içinde hazırlanan komut parametrelerinin güncellenmesi, satır başına amortize O(1) süre alır: O(M).

$$\text{Toplam Zaman Karmaşıklığı} = O(N + M) = O(N)$$

Toplam çalışma süresi dosya boyutuyla doğrusal ölçeklenir.

### Alan Karmaşıklığı (Space Complexity)

* **Bellek Kullanımı:** JSON modeli bellekte `JToken` yapısında tutulur ve O(N) ek alan gerektirir. `ParseToken` ile tek geçişli bellek kullanımı, kopya nesne oluşumunu önler.
* **Şema Üstverisi:** Şema sözlüğü, O(T \times C) oranında üstveri saklar (T tablo sayısı, C sütun sayısıdır). Bu alan veri hacmine kıyasla ihmal edilebilir düzeydedir.
* **Ampirik Gözlem:** 14 MB boyutundaki bir belge (`data.json`), çözümleme sırasında yaklaşık 60 ile 90 MB işlem belleği tüketir.

---

## Kıyaslama Veri Setleri (`240201025/Samples/`)

`240201025/Samples` dizini dört farklı test yapılandırması içerir:

* `simple.json`: Temel skaler değerlere sahip tek seviyeli nesne modeli.
* `nested.json`: İç içe geçmiş nitelikler ve sipariş kalemleri dizisi barındıran çok seviyeli model.
* `complex.json`: Fakülteler, öğretim üyeleri, ders programları ve öğrenci kayıtlarını kapsayan derin hiyerarşik üniversite veri modeli.
* `data.json`: Bellek yönetimi, düzleştirme ve toplu ekleme testleri için kullanılan büyük ölçekli kıyaslama veri seti (14,3 MB, 8.794 mobil cihaz kaydı ve derin `specs` nesneleri).

---

## Bağımlılıklar

* [.NET 9.0](https://dotnet.microsoft.com/)
* [Newtonsoft.Json (13.0.4)](https://www.nuget.org/packages/Newtonsoft.Json/): JSON belirteç dolaşımı ve serileştirme.
* [Microsoft.Data.Sqlite (10.0.8)](https://www.nuget.org/packages/Microsoft.Data.Sqlite/): SQLite veritabanı sürücüsü.
