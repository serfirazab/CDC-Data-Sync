# CLAUDE.md

Bu dosya, bu repoda çalışırken Claude Code'un her oturumda uyması gereken kuralları tanımlar.

## Proje Bağlamı

Domain, mimari ve faz planı için: @docs/project-domain-spec.md

Bu dosyaya (CLAUDE.md) domain/iş mantığı detayı eklenmeyecek — o dosyanın sorumluluğu ayrı tutulur. Burada sadece *her oturumda geçerli olan davranışsal kurallar* yer alır.

## Solution Yapısı

Tüm projeler **tek solution** altında (`DualWriteDemo.sln`). Yeni bir proje eklerken var olan solution'a dahil et, ayrı bir solution açma.

```
src/
├── DualWriteDemo.OrderService/
├── DualWriteDemo.InventoryService/
├── DualWriteDemo.Dashboard/
└── DualWriteDemo.Shared/
```

## Git Workflow

**Branch modeli:** Sadeleştirilmiş — `main`, `develop`, `feature/*`. `release`/`hotfix` branch'i yok.

- Yeni iş her zaman bir **GitHub Issue**'dan başlar.
- Feature branch `develop`'tan açılır: `feature/<issue-no>-kisa-aciklama` (örn. `feature/12-outbox-pattern`)
- PR'lar **develop**'a açılır. Doğrudan `main`'e commit veya PR **yok**.
- `develop` → `main` sadece stabil bir noktada, ayrı bir release PR'ıyla yapılır.
- PR açıklamasında ilgili issue'ya `Closes #<issue-no>` ile referans ver.
- **Merge stratejisi: Merge commit.** Squash veya rebase kullanma — PR ve issue geçmişinin git log'da ayrıştırılabilir kalması isteniyor.
- **Commit mesaj formatı: Conventional Commits.**
  - `feat: outbox tablosu ve transactional yazım eklendi`
  - `fix: kafka publish sırasında race condition düzeltildi`
  - `chore:`, `docs:`, `test:`, `refactor:` uygun şekilde kullanılır.
  - Commit mesajları İngilizce ya da Türkçe olabilir, tutarlı ol — proje boyunca ikisini karıştırma.
- Asla `main` veya `develop` üzerinde doğrudan çalışma; her zaman bir feature branch aç.

## Kod Stili (C# / .NET)

- **XML doc comment `<summary>` etiketleri kullanılmayacak.** Kod kendini açıklayıcı isimlendirmeyle anlatılsın; gerekiyorsa tek satır `//` yorum yeterli.
- **Nullable reference types açık** (`<Nullable>enable</Nullable>`), nullable uyarıları göz ardı edilmez.
- **File-scoped namespace** kullanılır (`namespace X;`), blok namespace değil.
- Yeni sınıflarda uygunsa **primary constructor** tercih edilir.
- Event/contract sınıfları `DualWriteDemo.Shared` altında toplanır, servisler arası kopya tanım oluşturulmaz.

## Test

- Postgres/Kafka gerektiren akışlar için entegrasyon testleri **Testcontainers** ile yazılır; mock'lanmış altyapı ile geçiştirilmez.
- Faz 1 ve Faz 2'deki fault injection senaryoları test edilebilir/otomatikleştirilebilir şekilde yazılır (manuel tıklamayla sınırlı kalmaz).
- Yeni bir feature PR'ı, ilgili değişikliği kapsayan en az bir test içermeden açılmaz.

## Yapılmaması Gerekenler

- `main`/`develop`'a doğrudan push yok.
- Issue'suz feature branch açma yok.
- Squash/rebase merge yok — sadece merge commit.
- `<summary>` tag'li XML doc comment yok.
- Yeni ayrı solution dosyası oluşturma yok — tek `DualWriteDemo.sln`.