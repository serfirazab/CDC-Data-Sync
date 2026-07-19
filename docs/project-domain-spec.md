# Proje Spesifikasyonu: Dual Write Problem Demo

## 1. Amaç

GitHub portfolyosu için, dağıtık sistemlerde sık karşılaşılan **dual write** (çifte yazma) problemini önce somut olarak sergileyen, ardından **Debezium (Change Data Capture) + Transactional Outbox Pattern** ile çözen bir referans proje.

Hedef sadece çalışan kod değil; sorunu adım adım ortaya koyan, GitHub Issue ile belgeleyen ve çözümü ayrı bir PR/feature olarak sunan **okunabilir bir mühendislik hikayesi** oluşturmak.

## 2. Problem Tanımı: Dual Write Nedir

Bir servis aynı iş operasyonu kapsamında iki farklı sisteme (örn. veritabanı + mesaj kuyruğu) ayrı ayrı, atomik olmayan şekilde yazdığında ortaya çıkan tutarsızlık riski. İki yazımdan biri başarılı olur, diğeri olmazsa (servis çöker, network hatası vb.) sistemler arasında kalıcı veri tutarsızlığı oluşur.

Bu projede somutlaştırma: **Sipariş oluşturma (DB yazımı) ile stok düşürme event'inin (Kafka publish) atomik olmaması.**

## 3. Domain Senaryosu: Sipariş & Stok Yönetimi

İki servis:
- **OrderService**: Sipariş oluşturma sorumluluğu. Siparişi kendi veritabanına yazar.
- **InventoryService**: Stok yönetimi sorumluluğu. `OrderCreated` event'ini dinleyip ilgili ürünün stoğunu düşürür.

Bu iki servis birbirinden bağımsız veritabanlarına sahip (mikroservis mantığı), bu yüzden aralarındaki senkronizasyon mesajlaşma (Kafka) üzerinden yürüyor.

## 4. Faz 1 — Naif İmplementasyon (Sorunu Sergileme)

### Davranış
`OrderService`, sipariş oluşturma isteğinde şu adımları **ayrı ayrı** yapar:
1. Sipariş kaydını kendi veritabanına yazar (`INSERT`).
2. Ayrı bir adım olarak Kafka'ya `OrderCreated` event'i publish eder.

Bu iki adım arasında **atomiklik garantisi yoktur**.

### Kasıtlı Hata Senaryosu (Fault Injection)
Dashboard üzerinden tetiklenebilen bir "chaos" mekanizması: adım 1 tamamlandıktan, adım 2'den (Kafka publish) hemen önce servisin çökmesini simüle et (örn. `Task.Delay` + exception fırlatma veya process'i öldürme).

### Sonuç
- Sipariş veritabanında **var**.
- `OrderCreated` event'i **hiç yayınlanmadı**.
- `InventoryService` bu siparişten habersiz, stok düşmedi.
- Dashboard'da bu tutarsızlık görünür hale getirilir (örn. "Sipariş var ama stok event'i yok" uyarısı).

### Belgeleme
Bu davranış bir **GitHub Issue** olarak açılır:
> Başlık: "Dual write inconsistency risk in OrderService"
> İçerik: sorunun teknik açıklaması, tetikleme adımları (repro steps), etkisi, önerilen çözüm yaklaşımı (outbox + CDC).

## 5. Faz 2 — Çözüm: Transactional Outbox + Debezium

### Yaklaşım
1. `OrderService`'in veritabanına bir **Outbox tablosu** eklenir (`OutboxMessages`).
2. Sipariş kaydı **ve** outbox event kaydı, **tek bir local database transaction** içinde birlikte yazılır. Artık atomiklik veritabanı seviyesinde garanti altında.
3. **Debezium**, Kafka Connect üzerinde çalışacak şekilde konumlandırılır ve Outbox tablosunu **CDC (Change Data Capture)** ile izler. Connector kaydı manuel bir adım olarak bırakılmaz — `docker-compose up` sonrası otomatik olarak (Kafka Connect health check'ini bekleyen bir init servisi ile) yapılır, böylece repo'yu klonlayan biri tek komutla projeyi ayağa kaldırabilir.
4. Outbox tablosuna yeni satır eklendiğinde Debezium bunu otomatik olarak Kafka topic'ine publish eder — uygulama kodu Kafka'ya hiç doğrudan yazmaz.
5. `InventoryService` tarafında **değişiklik gerekmez** — aynı topic'i dinlemeye devam eder. Garanti artık altyapı katmanından geliyor.

### Doğrulama
Faz 1'deki aynı fault injection senaryosu tekrar tetiklenir. Bu sefer:
- Transaction ya tamamen başarılı olur (sipariş + outbox kaydı birlikte) ya da tamamen geri alınır (rollback).
- Ara bir durum (sipariş var, event yok) **imkansız hale gelir**.
- Bu durum Blazor dashboard'da görsel olarak kanıtlanır.

### Kapanış
İlgili GitHub Issue, bu çözümü uygulayan PR ile kapatılır (`Closes #<issue-no>` referansı ile).

## 6. Mimari Bileşenler

| Bileşen | Sorumluluk |
|---|---|
| OrderService (Web API) | Sipariş oluşturma, Faz 2'de Outbox yazımı |
| InventoryService (Worker/Web API) | Event tüketimi, stok güncelleme |
| Dashboard (Blazor) | Görselleştirme, fault injection, event log izleme |
| Shared | Ortak event/contract sınıfları (`OrderCreated` vb.) |
| Debezium + Kafka Connect | CDC, Outbox tablosunu Kafka'ya köprüleme (sadece Faz 2) |
| PostgreSQL | OrderService ve InventoryService için ayrı veritabanları |
| Kafka | Servisler arası asenkron mesajlaşma |

## 7. Solution / Proje Yapısı

```
DualWriteDemo.sln
├── src/
│   ├── DualWriteDemo.OrderService/       (Web API)
│   ├── DualWriteDemo.InventoryService/   (Worker Service veya Web API)
│   ├── DualWriteDemo.Dashboard/          (Blazor Server veya WASM)
│   └── DualWriteDemo.Shared/             (event/contract sınıfları)
├── infra/
│   └── docker-compose.yml                (Postgres x2, Kafka, Kafka Connect+Debezium, Kafka UI)
├── docs/
│   └── project-domain-spec.md            (bu dosya)
└── DualWriteDemo.sln
```

## 8. Teknoloji Stack

- **.NET 8+**, C#
- **Blazor Server** (Kafka event log'unu canlı izlemek için doğal bir uyum sağlıyor — SignalR altyapısı zaten mevcut, backend'e direkt erişim var; WASM seçilseydi client-side'dan Kafka'ya erişim için ayrı bir API/WebSocket köprüsü gerekirdi, gereksiz karmaşıklık)
- **PostgreSQL** (Debezium'un native desteklediği, logical replication'a uygun bir veritabanı olduğu için tercih edilir)
- **Apache Kafka** + **Kafka Connect** + **Debezium PostgreSQL connector**
- **Docker Compose** ile tüm altyapının ayağa kaldırılması
- Test: **Testcontainers** ile entegrasyon testleri (Postgres + Kafka gerçek container'larda)

## 9. Blazor Dashboard Gereksinimleri

- Sipariş listesi (oluşturulma zamanı). Sipariş durumu (Status) tek değerle (`Created`) sabit tutulur — Confirmed/Cancelled/Delivered gibi bir yaşam döngüsü bu projenin kapsamında değil (bkz. Kapsam Dışı).
- Stok durumu listesi (ürün, mevcut miktar)
- Canlı event log: Dashboard kendi Kafka consumer'ını çalıştırır, tüketilen `order-created` mesajlarını zaman damgalı listeler.
- **Fault Injection**: `appsettings`'te `FaultInjection:Enabled` flag'i ile kontrol edilir (local/demo ortamında `true`, varsayılan/production config'te `false`). Flag açıkken dashboard'da bir "Crash Simulate Et" seçeneği görünür; sipariş oluşturma isteğine bu bilgi iletilir.
- **Tutarsızlık göstergesi**: Dashboard, kendi Kafka consumer'ından biriktirdiği event log'u kullanarak her siparişin OrderId'sinin bir `OrderCreated` mesajıyla eşleşip eşleşmediğini kontrol eder. Belirli bir eşik süre (örn. 5 sn) geçtiği halde eşleşme yoksa, sipariş satırında uyarı rozeti gösterilir. (İki servisin veritabanını doğrudan karşılaştırma yoluna gidilmez — InventoryService sipariş bazlı değil agregat stok tuttuğu için bu mümkün değildir.)

## 10. Kapsam Dışı (Out of Scope)

- Kullanıcı kimlik doğrulama / yetkilendirme
- Ürün katalog yönetimi (ürünler seed data ile sabit gelir)
- Ödeme entegrasyonu
- Production-grade Kubernetes deployment (Docker Compose yeterli)
- Çoklu para birimi, kargo, vb. e-ticaret detayları

## 11. Başarı Kriterleri (Definition of Done)

- [ ] Faz 1: Naif implementasyon çalışır durumda, fault injection ile tutarsızlık üretilebiliyor
- [ ] Faz 1: Sorun GitHub Issue olarak belgelenmiş
- [ ] Faz 2: Outbox + Debezium çözümü çalışır durumda
- [ ] Faz 2: Aynı fault injection senaryosunda tutarsızlık artık oluşmuyor
- [ ] Faz 2: İlgili Issue, çözüm PR'ı ile kapatılmış
- [ ] README: projenin ne olduğu, nasıl çalıştırılacağı (`docker-compose up` + servisler) ve dual write probleminin nasıl kanıtlanıp çözüldüğü açıkça anlatılıyor
- [ ] Tüm akış (Faz 1 → Issue → Faz 2 → PR → merge) gerçek git geçmişinde izlenebilir durumda

## 12. Not

Bu dosya proje domain'i ve mimarisini tanımlar. Git iş akışı kuralları, kod stil kuralları ve solution genelindeki davranışsal kısıtlamalar ayrı bir `CLAUDE.md` dosyasında tanımlanacaktır.