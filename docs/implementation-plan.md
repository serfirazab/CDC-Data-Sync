# Uygulama Planı: Dual Write Problem Demo

Bu dosya, `docs/project-domain-spec.md`'de tanımlanan projenin **teknik uygulama adımlarını** içerir. Code agent bu planı sırayla takip eder. Faz 1 tamamen bitmeden Faz 2'ye geçilmez.

## Faz 0 — Altyapı Kurulumu

- [ ] `docker-compose.yml` oluştur:
  - `postgres-orders` (OrderService veritabanı)
  - `postgres-inventory` (InventoryService veritabanı)
  - `kafka` (KRaft modu, Zookeeper'sız — daha az bileşen)
  - `kafka-connect` (Debezium PostgreSQL connector plugin'i yüklü image)
  - `kafka-ui` (topic/mesaj izleme için, geliştirme kolaylığı)
- [ ] `postgres-orders` için `wal_level=logical` ayarı (Debezium'un Faz 2'de ihtiyaç duyacağı, ama Faz 0'da baştan ayarlanması migration'ı kolaylaştırır)
- [ ] `infra/register-connector.sh`: Kafka Connect health check'i geçtikten sonra Debezium connector config'ini REST API'ye POST eden script. Docker Compose'da `kafka-connect`'e bağımlı, `depends_on` + health check ile otomatik çalışan bir `connector-init` servisi olarak tanımlanır (Faz 2'de aktif olacak, ama script Faz 0'da hazırlanabilir)
- [ ] Solution ve proje iskeletlerini oluştur (`DualWriteDemo.sln` + 4 proje, CLAUDE.md'deki yapıya göre)
- [ ] `DualWriteDemo.Shared` içine event contract'ları ekle (`OrderCreatedEvent`: OrderId, CustomerName, Items[ProductId, Quantity, UnitPrice], CreatedAt)

## Veri Modelleri

### OrderService DB (`orders_db`)
- `Orders`: Id (uuid, PK), CustomerName, TotalAmount, Status (tek değer: `Created`, yaşam döngüsü bu projenin kapsamında değil), CreatedAt
- `OrderItems`: Id (uuid, PK), OrderId (FK), ProductId, Quantity, UnitPrice

### InventoryService DB (`inventory_db`)
- `Products`: Id (uuid, PK), Name, StockQuantity
- Seed data: uygulama ayağa kalkarken teknoloji ürünleri temalı 5-10 örnek ürün otomatik eklenir (Laptop, Mouse, Keyboard, Monitor, Headphone, Webcam vb. — içerik önemsiz, migration seed veya startup seed ile eklenir)

## Faz 1 — Naif İmplementasyon (Sorunu Sergileme)

- [ ] **OrderService**: `POST /orders` endpoint'i
  - İstek: CustomerName + sipariş kalemleri (ProductId, Quantity)
  - Adım 1: `Orders` + `OrderItems` kaydı veritabanına yazılır (tek DB transaction, sadece bu servise özel)
  - Adım 2 (ayrı, atomik olmayan adım): `OrderCreatedEvent` Kafka'ya publish edilir
- [ ] **Fault injection mekanizması**: `appsettings`'te `FaultInjection:Enabled` flag'i ile kontrol edilir (local/demo ortamında `true`, varsayılan/production config'te `false`). Flag açıkken, istek body'sinde `simulateCrash: true` parametresi geldiğinde adım 1 başarıyla tamamlandıktan hemen sonra, adım 2'den (Kafka publish) önce exception fırlatılır. Flag kapalıyken bu parametre yok sayılır. Bu, "DB yazıldı ama event hiç gitmedi" durumunu deterministik şekilde üretir ve production config'inde kazara aktif olma riskini ortadan kaldırır.
- [ ] **InventoryService**: Kafka consumer, `order-created` topic'ini dinler, gelen event'teki her kalem için ilgili `Product.StockQuantity` değerini düşürür.
- [ ] **Dashboard (Blazor Server)**:
  - Sipariş oluşturma formu (ürün seç, adet gir, `FaultInjection:Enabled` açıkken görünen "Crash Simulate Et" checkbox'ı)
  - Sipariş listesi (OrderService DB'den, canlı/polling)
  - Stok listesi (InventoryService DB'den, canlı/polling)
  - Event log paneli: Dashboard'un **kendi Kafka consumer'ı**, tüketilen `order-created` mesajlarını zaman damgalı listeler (bu consumer aynı zamanda tutarsızlık tespiti için de kullanılır)
  - Tutarsızlık göstergesi: sipariş listesindeki her OrderId, event log'daki tüketilen mesajlarla eşleştirilir. Bir sipariş oluşturulduktan belirli bir süre (örn. 5 sn) sonra event log'da eşleşen bir `OrderCreated` mesajı yoksa, o sipariş satırında kırmızı bir uyarı rozeti gösterilir. (İki servisin veritabanı doğrudan karşılaştırılmaz.)
- [ ] **Doğrulama**: "Crash Simulate Et" işaretliyken sipariş oluştur → dashboard'da tutarsızlık rozetini gözlemle, ekran görüntüsü/GIF olarak README'ye eklenmek üzere not al
- [ ] **GitHub Issue aç**: "Dual write inconsistency risk in OrderService" — repro adımları, gözlemlenen davranış, önerilen çözüm (Outbox + Debezium) issue içeriğinde yer alır

## Faz 2 — Çözüm: Transactional Outbox + Debezium

- [ ] `orders_db`'ye `OutboxMessages` tablosu ekle: Id (uuid, PK), EventType (text), Payload (jsonb), CreatedAt, ProcessedAt (nullable, Debezium tarafından kullanılmaz ama izleme için tutulur)
- [ ] `POST /orders` akışını değiştir: `Orders` + `OrderItems` + `OutboxMessages` kaydı **tek transaction** içinde yazılır. Bu adımdan sonra kod, Kafka'ya **doğrudan publish etmez** — bu satır tamamen kaldırılır.
- [ ] `infra/register-connector.sh` içindeki connector config JSON'ını doldur ve Faz 0'da eklenen `connector-init` servisinin `docker-compose up` sonrası otomatik olarak Kafka Connect'e (`kafka-connect:8083/connectors`) POST etmesini sağla — manuel `curl` adımına gerek kalmaz. Config'de:
  - `table.include.list`: sadece `OutboxMessages`
  - **Outbox Event Router SMT** (Debezium'un outbox transform'u) kullanılarak, tablo satırındaki `Payload` doğrudan Kafka mesajının değeri olarak çıkar (wrapper CDC formatı değil, temiz event)
- [ ] Debezium'un ürettiği topic adını, InventoryService'in dinlediği `order-created` topic'iyle eşleştir (yönlendirme SMT config'inde topic adı sabitlenebilir)
- [ ] **InventoryService kodunda değişiklik yapılmaz** — aynı topic'i dinlemeye devam eder, sadece mesajın kaynağı (uygulama kodu → Debezium) değişmiştir
- [ ] Faz 1'deki aynı fault injection senaryosunu tekrar çalıştır: `simulateCrash: true` ile sipariş oluştur
  - Beklenen: transaction ya tamamen commit olur (Order + OrderItems + OutboxMessage birlikte) ya da tamamen rollback olur — ara durum oluşmaz
  - Dashboard'da tutarsızlık rozeti artık **hiç görünmemeli**
- [ ] PR açılırken açıklamaya `Closes #<issue-no>` eklenir

## Test Planı

- [ ] Faz 1 için: `simulateCrash: true` ile bir entegrasyon testi yaz — DB'de sipariş oluştuğunu ama event'in publish edilmediğini doğrula (bu test, sorunun var olduğunu kanıtlamak için bilerek yazılır, Faz 1 kod tabanında "geçer" — yani mevcut hatalı davranışı doğrular)
- [ ] Faz 2 için: aynı senaryoyu Testcontainers ile (gerçek Postgres container) tekrar test et — transaction'ın ya tamamen yazıldığını ya da hiç yazılmadığını doğrula. Debezium/Kafka Connect'in kendisini test ortamında ayağa kaldırmak karmaşıksa, bu katman manuel/docker-compose ile doğrulanabilir; asıl otomatik test **outbox transaction atomikliğine** odaklanır.
- [ ] InventoryService için: `order-created` topic'inden gelen bir event'in stok düşürdüğünü doğrulayan birim/entegrasyon testi

## Sıralama Notu

Adımlar checklist sırasına göre ilerlenir. Her checklist maddesi CLAUDE.md'deki git kurallarına göre (issue → feature branch → PR → merge commit) ayrı ayrı işlenir; büyük adımlar (örn. "Faz 1 — Naif İmplementasyon") birden fazla küçük PR'a bölünebilir.