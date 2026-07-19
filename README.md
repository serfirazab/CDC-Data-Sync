# CDC Data Sync — Dual Write Problem Demo

Bu proje, dağıtık sistemlerde sık karşılaşılan **dual write** (çifte yazma) problemini somut olarak sergileyen ve **Transactional Outbox Pattern + Debezium CDC** ile çözen bir referans uygulamadır.

## Hedef

Sadece çalışan kod değil; sorunu adım adım ortaya koyan, GitHub Issue ile belgeleyen ve çözümünü ayrı bir PR olarak sunan **okunabilir bir mühendislik hikayesi** oluşturmak.

## Problem: Dual Write Nedir?

Bir servis aynı işlem kapsamında iki farklı sisteme (örn. veritabanı + mesaj kuyruğu) ayrı ayrı, atomik olmayan şekilde yazarsa, iki yazımdan yalnızca biri başarılı olduğunda sistemler arasında **kalıcı veri tutarsızlığı** oluşur.

**Bu projedeki somut senaryo:**

1. Kullanıcı sipariş oluşturur
2. `OrderService` siparişi veritabanına kaydeder
3. `OrderService`, `OrderCreated` event'ini Kafka'ya yayınlar
4. `InventoryService` event'i tüketir ve stok düşer

> **Sorun:** Adım 2 başarılı ama adım 3 başarısız olursa (servis çöker, network hatası vb.) — sipariş DB'de var ama stok hiç düşmedi. Sistem tutarsız.

---

## Çözüm: Transactional Outbox + Debezium CDC

### Nasıl Çalışır?

```
OrderService DB (Transactional Outbox)
┌─────────────────────────────────────┐
│  Orders (Order + Items)             │
│  OutboxMessages (event payload)     │  ← Aynı DB transaction içinde
└──────────┬──────────────────────────┘
           │ CDC (logical replication)
           ▼
     ╔═══════════════╗
     ║   Debezium    ║  ← PostgreSQL WAL'i okur
     ╚═════╤═════════╝
           │ Outbox Event Router SMT
           ▼
      order-created topic (Kafka)
           │
           ▼
  InventoryService (Consumer)
  ┌──────────────────────┐
  │  StockQuantity -= Qty │
  └──────────────────────┘
```

1. Sipariş kaydı **ve** outbox event'i **tek bir DB transaction'ı** içinde yazılır
2. Uygulama kodu **Kafka'ya doğrudan yazmaz** — bu sorumluluk altyapıya devredilmiştir
3. **Debezium** PostgreSQL WAL'ini (CDC) okuyarak `outbox_messages` tablosundaki değişiklikleri yakalar
4. **Outbox Event Router SMT** event tipine göre (`event_type`) ilgili Kafka topic'ine yayınlar
5. `InventoryService` (değişiklik yapılmamıştır) aynı topic'i dinleyip stok düşürür

**Sonuç:** Veritabanı transaction'ı ya tamamen başarılı olur (sipariş + outbox kaydı birlikte) ya da tamamen rollback olur. Ara durum — *sipariş var, event yok* — **imkansızdır**.

---

## Mimari

### Servisler

| Bileşen | Port | Sorumluluk |
|---|---|---|
| **OrderService** | `5000` | Sipariş oluşturma, Outbox yazımı (Web API) |
| **InventoryService** | `5001` | `OrderCreated` event'ini tüketme, stok güncelleme (Web API) |
| **Dashboard** | `5075` | Görselleştirme, fault injection, event log (Blazor Server) |
| **DualWriteDemo.Shared** | — | Ortak event/contract sınıfları |

### Altyapı (Docker Compose)

| Bileşen | Rolü |
|---|---|
| `postgres-orders` (5432) | OrderService veritabanı — Outbox tablosu burada |
| `postgres-inventory` (5433) | InventoryService veritabanı |
| `kafka` (9092/9094) | Servisler arası mesajlaşma (KRaft mode) |
| `kafka-connect` (8083) | Kafka Connect + Debezium connector |
| `connector-init` | Health check bekleyip Debezium connector'ını otomatik kaydeder |
| `kafka-ui` (8080) | Kafka topic yönetimi (opsiyonel) |

---

## Kurulum ve Çalıştırma

### Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Terminal / PowerShell

### Adım Adım

```bash
# 1. Altyapıyı ayağa kaldır (PostgreSQL x2, Kafka, Kafka Connect + Debezium, Kafka UI)
cd infra
docker compose up -d

# 2. Altyapının hazır olmasını bekle (~60 saniye)
#    connector-init servisi, Kafka Connect health check'i geçtikten sonra
#    Debezium connector'ını otomatik kaydeder.
docker logs connector-init -f
# Çıktı: "Debezium connector registration complete"

# 3. Servisleri başlat (her biri ayrı terminalde)
cd src/DualWriteDemo.OrderService
dotnet run
# Now listening on: http://localhost:5000

cd src/DualWriteDemo.InventoryService
dotnet run
# Now listening on: http://localhost:5001

cd src/DualWriteDemo.Dashboard
dotnet run
# Now listening on: http://localhost:5075
```

> **Not:** Projeyi tamamen test etmek için üç servisin de aynı anda çalışıyor olması gerekir.

### Quick Test

```bash
# Normal sipariş (stok düşer)
curl -s -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName":"Test","items":[{"productId":"a1b2c3d4-0001-4000-8000-000000000001","quantity":1,"unitPrice":999.99}],"simulateCrash":false}'

# Crash sipariş (rollback — hiçbir şey yazılmaz)
curl -s -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName":"Crash","items":[{"productId":"a1b2c3d4-0001-4000-8000-000000000001","quantity":1,"unitPrice":999.99}],"simulateCrash":true}'
```

---

## Test Senaryoları

### ✅ Normal Akış

1. Dashboard'u aç → http://localhost:5075
2. **Create Order** sayfasına git
3. Müşteri adı gir, ürün seç (Laptop, Mouse vb.), miktar belirle
4. **Crash Simulate Et** kutusunu **işaretleme**
5. Submit'e bas
6. **Orders** sayfasında siparişi gör
7. Siparişin yanında **yeşil "Event Received" rozeti** varsa → Event Log'a düştü demek
8. **Inventory** sayfasında stok düştüğünü doğrula

### 🔴 Fault Injection — Tutarsızlık Testi (Faz 2'de Çözüm)

1. **Create Order** sayfasına git
2. **Crash Simulate Et** kutusunu **işaretle**
3. Submit'e bas
4. **Hata mesajı gör**: `Simulated crash — transaction rolled back` (Faz 1) veya `transaction rolled back. No data was written` (Faz 2)
5. **Orders** sayfasını kontrol et → crash siparişi **listede yok**
6. **Inventory** sayfasını kontrol et → stok **değişmemiş**
7. **Event Log** sayfasını kontrol et → crash event'i **kayıtlı değil**

### Faz 1 vs Faz 2 Farkı

| Durum | Faz 1 | Faz 2 (Outbox + CDC) |
|---|---|---|
| Normal sipariş | ✅ DB + Kafka başarılı | ✅ DB + Kafka başarılı |
| Crash sonrası DB | ❌ Sipariş var | ✅ Sipariş **yok** (rollback) |
| Crash sonrası Event | ❌ Kafka'ya hiç gitmedi | ✅ Kafka'ya hiç gitmedi (tutarlı) |
| Tutarsızlık | 🔴 **Var** | 🟢 **Yok** |

---

## Proje Yapısı

```
DualWriteDemo.sln
├── src/
│   ├── DualWriteDemo.OrderService/          # Web API — sipariş + outbox yazımı
│   ├── DualWriteDemo.InventoryService/      # Web API — Kafka consumer + stok
│   ├── DualWriteDemo.Dashboard/             # Blazor Server UI
│   └── DualWriteDemo.Shared/                # Ortak event/contract sınıfları
├── infra/
│   ├── docker-compose.yml                   # Tüm altyapı
│   └── register-connector.sh               # Debezium connector kayıt scripti
├── docs/
│   └── project-domain-spec.md               # Proje spesifikasyonu
├── CLAUDE.md                                # Kod stili ve git kuralları
└── README.md                                # Bu dosya
```

---

## Teknoloji Stack

| Teknoloji | Kullanım Amacı |
|---|---|
| .NET 10 + C# | Backend servisler ve dashboard |
| Blazor Server | Dashboard (SignalR ile canlı event log) |
| PostgreSQL | OrderService ve InventoryService veritabanları |
| Apache Kafka | Servisler arası asenkron mesajlaşma |
| Debezium + Kafka Connect | CDC ile Outbox → Kafka köprüsü |
| Docker Compose | Tüm altyapının tek komutla ayağa kalkması |
| Testcontainers | Entegrasyon testleri (gerçek Postgres + Kafka container'larında) |

---

## Git Geçmişi

Projenin hikayesi git log'da izlenebilir:

```
# Faz 1 — Naif implementasyon (sorun sergileniyor)
cffeeca feat: Faz 1 naif implementasyon — dual write sorununu sergileme
d9e59a3 fix: test sirasinda tespit edilen hata duzeltmeleri

# Faz 2 — Outbox + Debezium (çözüm)
04e9abb feat: Faz 2 outbox + Debezium — dual write cozumu
09e6631 fix: test sirasinda tespit edilen sorunlar duzeltildi
f7dd761 feat: Faz 2 - Transactional Outbox + Debezium ile dual write cozumu  ← merge commit
```

İlgili GitHub Issue'ları:
- [#5](https://github.com/serfirazab/CDC-Data-Sync/issues/5) — Dual write inconsistency risk
- [#6](https://github.com/serfirazab/CDC-Data-Sync/issues/6) — Outbox Pattern ile çözüm

---

## Lisans

MIT
