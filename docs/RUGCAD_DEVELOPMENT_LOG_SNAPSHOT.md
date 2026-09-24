# RugScale development history imported from RugCAD

This is the source-development log snapshot captured from `yldrmhasan/RugCAD` branch `chatgpt/rugcad-work-2026-09-22` before RugScale ownership was removed from RugCAD. It is retained for migration/audit history; current standalone architecture is documented in the other RugScale docs.

---

# RugCAD — Güncel Durum ve Geliştirme Günlüğü

> **Yetkili güncel özet — 22 Eylül 2026 / main:** Bu dosyanın devamındaki bölümler kronolojik geliştirme günlüğüdür; daha eski maddeler daha yeni maddeler tarafından geçersiz kılınmış olabilir. Güncel mimari için `04-teknoloji-ve-platform-karari.md`, Workspace/File Browser/View sözleşmesi için `06-workspace-file-browser-ux-2026-09-21.md` esas alınmalıdır. PR #4 (`feature/performance-ux-design-compare`) `main`e merge edilmiştir.

## 2026-09-22 konsolide güncel durum

- File Browser performans regresyonları giderildi: hidden-overlay cancellation, active-tab lazy load, background filesystem metadata, cancellable scans, grid-only bounded thumbnails, 128-entry retained preview limiti ve async network/drive discovery.
- Custom VirtualizingWrapPanel için container `Recycle` kullanılmıyor; scroll sonrası blank-grid ürettiği kullanıcı testinde doğrulandığı için güvenli `Remove` yolu korunuyor.
- DesignCanvas static view/grid event abonelikleri Loaded/Unloaded yaşam döngüsüne bağlandı; kapatılan canvas'ların bellekte tutulması engellendi.
- Pan edge-bounce incremental delta ile düzeltildi. Off-canvas serbestliği yalnız Pan tool + sol drag ve Alt+Space + sol drag için geçerli; wheel/keyboard/scrollbar/middle-mouse strict sınırda.
- `View > Fit All Designs to Window` eklendi; finite minimum zoom %20 / yaklaşık `fitZoom*0.8` seviyesine genişletildi.
- Design Comparison farklı native ebatlarda normalize image-focus pan/zoom kullanıyor; scrollbar yüzdesi senkronu kaldırıldı.
- Windows taskbar ana pencere ikonu artık şeffaf EXE `ApplicationIcon` (`rugcad.ico`) üzerinden geliyor; `rugcad.png` Window.Icon override kaldırıldı.

---

# RugCAD — Devam Notu / Proje Durumu

**Amaç:** Bu döküman, konuşmayı devralacak başka bir AI/geliştiricinin hiçbir önceki bağlam
olmadan kaldığımız yerden devam edebilmesi için hazırlandı. Aşağıdaki sıra ile okunmalı:
1. Bu dosya (genel durum + hemen yapılacak iş)
2. `docs/01-arastirma-ve-analiz.md` (proje amacı, Texcelle özellik envanteri, kapsam katmanları)
3. `docs/02-teknik-derinlik-simulasyon-ve-uretim.md` (doku simülasyonu + üretim entegrasyonu detayları)
4. `docs/03-release-notes-bulgulari.md` (ek özellik fikirleri + orijinal ürünün teknoloji ipuçları)
5. `docs/04-teknoloji-ve-platform-karari.md` (teknoloji/platform kararı ve gerekçeleri)

---

## 1. Proje Nedir?

RugCAD, `C:\Program Files (x86)\NedGraphics\Texcelle 2009` altında kurulu **Texcelle**
(halı/tekstil desen tasarım CAD yazılımı) programının klonu olarak başlayıp üzerine yeni
özellikler eklenecek bir masaüstü uygulaması. Kullanıcı (proje sahibi) ile birlikte önce
kapsamlı bir araştırma yapıldı (Texcelle'in kurulu dosyaları, yardım içeriği, PDF kılavuzları
incelendi), sonra teknoloji kararı verildi, şimdi de gerçek kodlama/iskelet aşamasındayız.

## 2. Teknoloji Kararı (özet — detay için docs/04)

- **Platform:** Sadece Windows masaüstü.
- **Runtime/UI:** **.NET 8 + WPF (C#)**.
- **Çizim motoru:** **SkiaSharp** (`SkiaSharp.Views.WPF` paketi) — piksel-hassas, GPU hızlandırmalı.
- **Panel/Docking:** **AvalonDock** (`Dirkster.AvalonDock` + `Dirkster.AvalonDock.Themes.VS2013`,
  her ikisi de ücretsiz/MIT) — Photoshop tarzı taşınabilir paneller istendiği için MVP'ye dahil
  edildi.
- **Tema:** Dark/Light — WPF bunu native desteklemiyor, elle yönetiliyor (bkz. Bölüm 5).
- **ÖNEMLİ KARAR — DevExpress kullanılmıyor:** Bunun yerine WPF + ücretsiz/açık kaynak kütüphaneler kullanılıyor,
  eksik kalan noktalarda ihtiyaç oldukça yeni ücretsiz kütüphane eklenecek. **Bu kısıtı asla
  unutma — DevExpress veya başka lisanslı bir bileşen önerme/ekleme.**
- **Test:** xUnit (sadece `RugCAD.Core` için; UI testi MVP kapsamı dışı).

## 3. Proje Yapısı

```
RugCAD.sln
├── src/RugCAD.Core/          (net8.0, WPF'ten tamamen bağımsız, framework-agnostic çekirdek)
│   ├── Models/
│   │   ├── RugColor.cs        — basit RGB record struct
│   │   ├── Palette.cs         — indexed-color palet listesi
│   │   └── DesignDocument.cs  — piksel ızgarası (byte[] palet index'leri) + Width/Height/Palette
│   ├── Commands/
│   │   ├── IDesignCommand.cs      — Do()/Undo() arayüzü
│   │   ├── PaintStrokeCommand.cs  — bir "stroke"taki tüm piksel değişikliklerini tek undo
│   │   │                            adımı olarak toplar. AddPixel() ÇAĞRILDIĞI ANDA pikseli
│   │   │                            gerçekten uygular (canlı önizleme için) VE undo/redo
│   │   │                            kaydını tutar.
│   │   └── CommandHistory.cs      — klasik undo/redo stack (Execute/Undo/Redo)
│   └── Drawing/
│       ├── Rasterizer.cs      — Line (Bresenham), RectangleOutline, EllipseOutline (açı
│       │                        örneklemeli, midpoint algoritması değil ama görsel olarak
│       │                        aynı sonucu veriyor)
│       └── FloodFill.cs       — 4-komşuluklu flood fill (bucket aracı için)
│
├── src/RugCAD.App/           (net8.0-windows, WPF UI)
│   ├── MainWindow.xaml(.cs)   — Menü (sabit) + 2 ToolBar (Undo/Redo, çizim araçları) +
│   │                            AvalonDock DockingManager (Active Pattern paneli solda,
│   │                            tuval ortada LayoutDocument içinde, Color Palette sağda)
│   ├── Controls/
│   │   ├── DesignCanvas.xaml(.cs) — SkiaSharp SKElement ile piksel render + fare etkileşimi.
│   │   │                            İçinde alt "status bar" da var (isim/boyut/warp-weft
│   │   │                            density solda, hover piksel koordinat+renk+index sağda).
│   │   │                            TÜM ÇİZİM ARAÇLARININ MOUSE MANTĞI BURADA.
│   │   └── PalettePanel.xaml(.cs) — renk paleti kutuları (içinde index numarası yazıyor,
│   │                                 arka plan parlaklığına göre siyah/beyaz yazı rengi seçiliyor)
│   ├── ViewModels/
│   │   ├── DesignSurfaceViewModel.cs — UI tarafının ana view-model'i: DesignDocument'i sarar,
│   │   │                               CommandHistory'yi tutar, palet swatch listesini,
│   │   │                               seçili aracı (CurrentTool), seçili rengi (SelectedPaletteIndex),
│   │   │                               Undo/Redo/SelectTool komutlarını expose eder.
│   │   ├── PaletteSwatchViewModel.cs  — tek bir palet kutusu (Index, Brush, IsSelected, TextBrush)
│   │   └── RelayCommand.cs            — basit ICommand implementasyonu
│   ├── Tools/
│   │   └── DrawTool.cs — enum: Pencil, Eyedropper, Bucket, Line, Rectangle, Ellipse, Selection
│   │                     (Selection AZ ÖNCE EKLENDİ, henüz UI/mantık tarafı YOK — bkz. Bölüm 4)
│   ├── Theming/
│   │   ├── ThemeManager.cs         — Light/Dark ResourceDictionary swap + ThemeChanged event
│   │   └── WindowChromeTheming.cs  — DWM API ile pencere başlık çubuğunu karanlık/aydınlık yapma
│   ├── Themes/
│   │   ├── Light.xaml / Dark.xaml  — sadece renk/fırça tanımları (App.Background, App.Foreground,
│   │   │                             App.PanelBackground, App.Border, App.CanvasBackground,
│   │   │                             App.AccentBrush)
│   │   └── ControlStyles.xaml      — Menu/MenuItem/ToolBar/Button/ToggleButton için ÖZEL
│   │                                 ControlTemplate'ler (WPF'in varsayılan Aero2 teması bu
│   │                                 kontrollerde DynamicResource'u görmezden geldiği için
│   │                                 şablonların TAMAMEN değiştirilmesi gerekti — bkz. Bölüm 5)
│   └── Converters/
│       ├── BoolToBrushConverter.cs — palet kutusu seçili kenarlığı için
│       └── EnumEqualsConverter.cs  — toolbar'daki ToggleButton'ların "aktif araç" vurgusu için
│
└── tests/RugCAD.Core.Tests/  (xUnit, 11 test, hepsi geçiyor)
    ├── PaintStrokeCommandTests.cs
    └── DrawingTests.cs (Rasterizer + FloodFill testleri)
```

## 4. Selection (Seçim) Aracı — TAMAMLANDI (Photoshop-tarzı, maske tabanlı)

Pencil/Eyedropper/Bucket/Line/Rectangle/Ellipse'den sonra **Selection aracı tamamlandı**, sonra
kullanıcı isteğiyle **tam Photoshop davranışına** yükseltildi (Shift/Ctrl ile ekle/çıkar/kesişim,
sürükleyerek taşıma/kopyalama). Derlendi (0 hata), 11/11 Core testi geçti, uygulama çalışır
durumda doğrulandı.

### 4.1 Mimari karar: dikdörtgen değil, **piksel maskesi**

İlk versiyon `Selection`'ı tek bir `(X,Y,Width,Height)` dikdörtgeni olarak tutuyordu. Kullanıcı
"seçime ekle/çıkar" (Shift/Ctrl+sürükle ile birleşik, L-şekilli seçimler) istediğinde bu yetersiz
kaldığı için **`DesignSurfaceViewModel._selectionMask` bir `bool[,]` (tüm doküman boyutunda)
haline getirildi.** `Selection` property'si artık sadece bu maskenin **bounding box**'ı (UI/marquee
çizimi ve kaba "aralıkta mı" kontrolleri için); gerçek şekli sorgulamak için her zaman
`IsPixelSelected(x,y)` kullanılmalı. Bu, ileride poligon/lasso seçim eklenirse de aynı modele
oturacak şekilde tasarlandı (bkz. `docs/01` Texcelle envanterinde "Polygonal Selection",
"Elliptical Selection").

### 4.2 Nihai etkileşim kuralları (kullanıcı ile birlikte kararlaştırıldı)

**Yeni seçim çizerken** (Shift/Alt basılıysa HER ZAMAN, DEĞİLSE sadece tıklanan piksel seçili
DEĞİLSE → `SelectionDragMode.NewSelection`; yani Shift/Alt basılıyken tıklanan nokta mevcut
seçimin içi ya da dışı olması FARK ETMEZ, her zaman ekle/çıkar/kesişim moduna girilir — kullanıcı
isteği: "seçimin içine tıklasan bile Shift/Alt ile eksiltme/arttırma yapabilmeliyim, taşımaya
geçmemeli"):
| Tuş | Mod | Sonuç |
|---|---|---|
| (yok) | `Replace` | Eskisinin yerine yeni seçim |
| Shift | `Add` | Yeni dikdörtgen mevcut seçime eklenir (birleşim) |
| Alt | `Subtract` | Yeni dikdörtgen mevcut seçimden çıkarılır (fark) |
| Shift+Alt | `Intersect` | Sadece iki bölgenin kesişimi seçili kalır |

`DesignCanvas.OnMouseLeftButtonDown`'daki `wantsNewMarquee` bayrağı (Shift veya Alt **tıklama
anında** basılı mı) bu kuralı uyguluyor: `wantsNewMarquee` true ise `IsPixelSelected` kontrolüne
HİÇ BAKILMADAN doğrudan `NewSelection` moduna girilir; sadece `wantsNewMarquee` false VE
tıklanan piksel gerçekten seçiliyse `Move` (floating) moduna girilir. Bkz. Bölüm 10.2 için Ctrl'in
şekil-merkezleme rolü ve Shift'in "hem Add HEM kare" ikili rolünün nasıl çakışmadan bir arada
çalıştığının tam açıklaması.

Sürüklerken önizleme rengi moda göre değişir: Add=yeşil, Subtract=turuncu-kırmızı,
Intersect=camgöbeği, Replace=beyaz (bkz. `DesignCanvas.ColorForCombineMode`). **Not:**
Photoshop'un kendisi de sürükleme sırasında birleşim/farkı CANLI göstermez, sadece bırakınca
uygular — biz de aynı şekilde davranıyoruz (mevcut seçimin gerçek şekli sürükleme sırasında ayrı
bir marching-ants olarak sabit gösteriliyor, yeni dikdörtgen üstüne bindirilmiş ayrı bir
önizleme olarak çiziliyor; birleşim sadece `EndStroke`'ta hesaplanıyor).

**Mevcut seçimin İÇİNDEKİ bir piksele tıklayıp sürüklerken** (→ `SelectionDragMode.Move`) — **bkz.
4.2c, bu davranış bir "floating" (yüzen) modele yükseltildi, aşağıdaki tablo NİHAİ halidir:**
| Tuş | Bırakınca ne olur |
|---|---|
| (yok) | **HİÇBİR ŞEY desene işlenmez** — içerik sadece yeni konumda "dinlenmeye" geçer (`RepositionFloat`), tekrar tekrar sürüklenebilir |
| Ctrl | **Kes/Taşı** — hemen ve kesin olarak bakılır: kaynak temizlenir (index 0), içerik son konuma yazılır (`FinalizeFloatAsMove`), floating hali sona erer |
| Shift (ikisiyle de kombinlenebilir) | Sürüklemenin tuval sınırının dışına çıkmasına izin verir (bkz. 4.2b) |

İçerik nihayet desene işlenmesi için (yani "bake" olması için) üç yol var:
1. **Ctrl+sürükle bırakma** → yukarıdaki gibi anında Kes/Taşı olarak işlenir.
2. **Edit → Duplicate Selection (Ctrl+J)** → mevcut (yüzen) konumda bir KOPYA işlenir, orijinal
   (hâlâ ilk konumunda, hiç dokunulmamış) yerinde kalır (`FinalizeFloatAsDuplicate`).
3. **Seçimi bırakma/değiştirme** (Esc, Select None, yeni bir seçim başlatma, Selection dışında
   bir araca geçme) → örtük olarak (2)'deki gibi **kopya olarak** bake edilir
   (`FinalizePendingFloatIfAny` → `FinalizeFloatAsDuplicate`). Yani "release = duplicate" kuralı.

**ÖNEMLİ — bu klasik Photoshop'un TERSİ (kullanıcı bilinçli tercihi, ÜÇ KEZ netleşti,
sessizce eskiye çevirme):** Photoshop'ta düz sürükleme gerçek zamanlı taşımadır ve her bırakışta
anında desene işlenir. Burada düz sürükleme SADECE ÖNİZLEME/YENİDEN KONUMLANDIRMADIR, hiçbir
zaman kendiliğinden desene işlenmez — kullanıcı ne kadar çok kez tutup bırakırsa bıraksın (Ctrl
kullanmadığı sürece) desende hiçbir kalıcı iz oluşmaz. Bu, "her bırakışta önceki konumda da bir
kopya kalmış" şeklindeki gerçek bir kullanıcı hatası bildirimi üzerine böyle tasarlandı (bkz. 4.2c).

#### 4.2b Tuval sınırı — veri kaybını önleme (kullanıcı buldu, düzeltildi)

**Bulunan hata:** Seçim tuval dışına sürüklenip bırakıldığında, dışarı taşan pikseller kalıcı
olarak siliniyordu (Document'in o koordinatlarda hiç yeri yok), ve kullanıcı seçimi geri
sürüklediğinde "seçim küçülmüş" görünüyordu (sadece hayatta kalan kısım).

**Çözüm — iki katmanlı:**
1. **Varsayılan davranış (Shift YOK):** `DesignSurfaceViewModel.ClampOffsetToBounds(sel, dx, dy)`
   sürükleme ofsetini, seçimin bounding box'ı tuval sınırını asla aşmayacak şekilde kırpar
   (`Math.Clamp`). Yani seçim artık kenara "yapışır", dışarı hiç çıkamaz — veri kaybı yapısal
   olarak imkansız hale geldi. Bu kırpma hem canlı önizlemede (`DrawSelectionOverlay`) hem de
   gerçek commit'te (`MoveSelection`/`DuplicateSelection`) aynı şekilde uygulanıyor, yani
   önizleme ile sonuç arasında sürpriz yok.
2. **Kullanıcı isteği: "Shift basılıyken tuval dışına çıkabilsin ama veri kaybı olmasın."**
   Gerçek bir "sınırsız pasteboard" (Photoshop'un tuval dışına taşan içeriği hafızada tutan
   alanı) modeli kurmak, `_selectionMask`'i Document boyutundan bağımsız hale getirmeyi
   gerektirirdi — kapsam/risk dengesi için bu YAPILMADI. Bunun yerine pragmatik bir çözüm
   uygulandı: Shift basılıyken kırpma YAPILMIYOR (önizleme de gerçekten dışarı taşıyor), ama
   commit anında **taşınan/kopyalanan içeriğin TAMAMI önce panoya (`_clipboard`) yedekleniyor**
   (`StashAsClipboardSafetyNet`). Böylece tuval dışında kalan kısım Document'ten silinse bile,
   kullanıcı her zaman Ctrl+V ile TÜM içeriği (dışarı taşan kısım dahil) geri getirebiliyor —
   veri gerçek anlamda hiçbir zaman kaybolmuyor, sadece "görünürde seçili değil" hale geliyor.
   **Bu, Photoshop'un gerçek pasteboard davranışının BİREBİR AYNISI DEĞİL** (orada içerik
   görünmeye devam eder, burada Paste'e kadar "gizli" kalır) — kullanıcıya bu farkı netleştir,
   eğer tam pasteboard davranışı isterse bu ayrı, daha büyük bir iş (bkz. yukarıdaki "yapılmadı"
   notu — `_selectionMask`'in Document-bağımsız, kendi orijini olan bir yapıya dönüştürülmesi
   gerekir).
   `PasteClipboard()` de aynı mantıkla, yapıştırma konumunu tuval sınırına göre kırpıp (`Math.Clamp`)
   benzer bir veri kaybını önceden engelliyor.

#### 4.2c "Floating Selection" mimarisine yükseltme (kullanıcı buldu, ikinci büyük düzeltme)

**Bulunan hata (kullanıcının kelimeleriyle):** "bir yeri seçiyorum ve bir yere taşıyorum ve
tekrar taşımak istediğimde daha önceki bıraktığım yere işlemiş görüyorum" — yani önceki
implementasyonda HER bırakış (`DuplicateSelection`/`MoveSelection`) anında `PaintIndexedPixels`
ile Document'e kalıcı olarak yazılıyordu. Kullanıcı bir seçimi art arda birkaç kez taşıdığında,
her ara durak deside kalıcı bir kopya bırakıyordu. İstenen: **"kullanıcı duplicate veya seçimi
bırakmadığı sürece desene işlemesin."**

**Çözüm — gerçek bir floating (yüzen) seçim durumu eklendi:**
- `DesignSurfaceViewModel` içine `FloatingSelection` (private nested class) eklendi:
  `Capture` (şekil+renkler, sabit), `OriginalX/OriginalY` (içeriğin Document'te HÂLÂ fiziksel
  olarak durduğu yer — floating süresince hiç temizlenmez), `CurrentX/CurrentY` (kullanıcının
  şu ana kadar sürükleyerek getirdiği konum — sadece kavramsal, Document'e hiç yazılmaz).
- `_floating` alanı `null` iken sistem eskisi gibi çalışır (`_selectionMask`/`_bakedBounds`
  üzerinden "baked" bir seçim). `_floating` doluyken `Selection` property'si ve
  `IsPixelSelected(x,y)` OTOMATİK OLARAK floating durumuna göre cevap verir (bkz. kod) — yani
  DesignCanvas'ın "tıklanan piksel gerçekten seçili mi" kontrolü floating sırasında da doğru
  çalışır, kullanıcı aynı yüzen içeriği tekrar tekrar tutup taşıyabilir.
- Yeni public API: `BeginFloatIfNeeded()` (ilk sürüklemede baked seçimi Document'ten okuyup
  yüzdürür; zaten yüzüyorsa var olanı döndürür — yeniden okuma yapmaz), `RepositionFloat(dx,dy)`
  (SADECE `CurrentX/Y`'yi günceller, Document'e DOKUNMAZ), `FinalizeFloatAsMove(dx,dy,allowOffCanvas)`
  (kaynağı `OriginalX/Y`'den temizler + son konuma yazar, TEK undo adımı, floating'i bitirir),
  `FinalizeFloatAsDuplicate()` (sadece son konuma yazar, kaynağa DOKUNMAZ, floating'i bitirir),
  `FinalizePendingFloatIfAny()` (varsa floating'i `FinalizeFloatAsDuplicate` ile bake eder —
  "release = duplicate" kuralının uygulandığı tek nokta).
- `FinalizePendingFloatIfAny()` şu noktalardan çağrılıyor (yani "seçimi bırakma" bunların HER
  BİRİNİ kapsıyor): `ClearSelection()` başı, `ApplySelectionRect()` başı (yeni bir marquee
  başlatmak eskiyi bırakır), `CurrentTool` setter'ı (Selection aracından başka bir araca
  geçerken).
- `DesignCanvas.xaml.cs`: artık kendi `_moveCapture` alanını TUTMUYOR — her ihtiyaç duyduğunda
  `_viewModel.FloatingCapture`'a soruyor (tek doğruluk kaynağı ViewModel'de). Render mantığı
  (`DrawSelectionOverlay`) artık "sürükleniyor mu" fark etmeksizin, floating VARSA HER ZAMAN o
  içeriği `Selection` konumunda çiziyor (yeni `DrawCaptureOutline` yardımcı metoduyla, gerçek
  maske şeklini izleyerek) — çünkü floating iken Document hiç değişmediği için idle render'ın
  tek bilgi kaynağı bu overlay.
- `EndStroke`'taki Move dalı artık: Ctrl YOKSA → `RepositionFloat` (commit yok); Ctrl VARSA →
  `FinalizeFloatAsMove` (anında commit, floating biter).
- `DuplicateInPlace()` (Ctrl+J) artık floating-farkında: floating varsa `FinalizeFloatAsDuplicate()`
  çağırıyor; yoksa eskisi gibi (baked seçimi olduğu yerde damgalıyor, no-op).
- `DeleteSelection()` floating-farkında: floating varsa, floating'i atıp `OriginalX/Y`'deki
  GERÇEK (hâlâ dokunulmamış) içeriği temizliyor (kullanıcının "bunu sil" niyetini karşılamak için).
- `CopySelection()` floating-farkında: floating varsa doğrudan `_floating.Capture`'ı panoya alıyor.

**Sonuç:** Artık kullanıcı bir seçimi istediği kadar sürükleyip bırakabilir, desende HİÇBİR İZ
kalmaz; sadece Ctrl+sürükle (anında kes/taşı), Ctrl+J (anında kopyala) veya seçimi bırakma
(dolaylı kopyala) ile içerik kalıcı hale gelir.

### 4.3 Dosya bazında değişiklikler

**Not:** Bu liste ilk (basit, "her bırakışta anında commit") versiyona ait. Sonradan tüm
Move/Duplicate mekanizması **floating (yüzen) seçim** mimarisine yükseltildi — bkz. **4.2c**
için nihai API'ler (`BeginFloatIfNeeded`, `RepositionFloat`, `FinalizeFloatAsMove`,
`FinalizeFloatAsDuplicate`, `FinalizePendingFloatIfAny`). Aşağıdaki `MoveSelection`/
`DuplicateSelection`/`CaptureSelectionPixels` (public) isimleri artık KODDA YOK, kaldırıldı —
sadece genel dosya/kavram haritası olarak faydalı, satır satır güncel değil.

- **`ViewModels/SelectionTypes.cs`** (yeni dosya): `SelectionCombineMode` enum (Replace/Add/
  Subtract/Intersect) ve `SelectionCapture` sınıfı (`Mask` + `Values`, ikisi de `[relY,relX]`
  boyutunda — bir taşıma/kopyalama/yapıştırma işleminden ÖNCE seçimin şeklini VE piksel
  değerlerini donduran anlık görüntü).
- **`ViewModels/DesignSurfaceViewModel.cs`**: `_selectionMask` (bool[,]), `Selection` (sadece
  bounding box, private set), `IsPixelSelected(x,y)`, `SetSelection` (=`ApplySelectionRect` +
  Replace), `ApplySelectionRect(x,y,w,h,mode)` (asıl birleştirme mantığı), `RecomputeSelectionBounds`
  (maskeden bounding box'ı yeniden hesaplar, maske tamamen boşalırsa `null`'a düşer),
  `EnumerateSelectedPoints()` (mask-aware iterasyon — sadece gerçekten seçili hücreler),
  `CopySelection/CutSelection/DeleteSelection/PasteClipboard` (hepsi mask-aware),
  `MoveSelection(dx,dy,capture)` / `DuplicateSelection(dx,dy,capture)` (ikisi de `SelectionCapture`
  alır; Move önce kaynağı `EnumerateSelectedPoints()` ile temizler sonra `AppendCapturePixels`
  ile hedefe yazar, Duplicate sadece hedefe yazar), `CaptureSelectionPixels()`,
  `GetSelectionOutlineEdges()` (marching-ants için: her seçili hücrenin komşularına bakıp hangi
  kenarının "dışarı" baktığını söyler — gerçek şekli, sadece bounding box'ı değil, çiziyor),
  `PaintIndexedPixels` (her piksele ayrı hedef index verilebilen genelleştirilmiş versiyon;
  `PaintPixels` artık bunun üzerine yazıldı), `ClampOffsetToBounds(sel,dx,dy)` (bkz. 4.2b),
  `DuplicateInPlace()` + `DuplicateInPlaceCommand` (Edit menüsü "Duplicate Selection", Ctrl+J —
  seçimi AYNI konumda desene damgalar, seçimi kaldırmaz; tek katmanlı düz dokümanda piksel
  bazında gerçek bir no-op ama layer eklendiğinde anlamlı hale gelecek, bkz. Bölüm 9).
- **`Controls/DesignCanvas.xaml.cs`**: `_moveCapture` artık `SelectionCapture?` tipinde;
  `_pendingCombineMode` alanı (yeni-seçim sürüklemesi başlarken `GetCombineModeFromModifiers()`
  ile hesaplanıp saklanıyor); `OnMouseLeftButtonDown`'da hit-test artık bounding-box değil
  `vm.IsPixelSelected(x,y)` (L-şekilli seçimlerde doğru çalışması için); `DrawSelectionOverlay` +
  `DrawSelectionOutline` + `GetSelectionOutlineEdges` tüketimi ile gerçek şekilli marching-ants;
  `EndStroke`'ta Ctrl (move/cut) ve Shift (tuval dışına izin) kontrolüyle Move/Duplicate çağrısı.
- **`MainWindow.xaml`**: toolbar'a "Selection" ToggleButton, Edit menüsüne Copy/Cut/Paste/Delete/
  Duplicate Selection (Ctrl+J)/Select All/Select None, `Window.InputBindings` ile
  Ctrl+C/X/V/A/J, Delete, Escape kısayolları.

### 4.4 Bilinen sınırlamalar (bilinçli olarak MVP dışı bırakıldı)

- Sadece dikdörtgen sürükleyerek seçim çiziliyor (Texcelle'deki gibi poligon/elips seçim ŞEKLİ
  yok) — ama maske altyapısı zaten poligon/lasso eklenmeye hazır, sadece yeni bir "aracın
  ürettiği nokta listesini maskeye uygula" yolu eklemek yeterli olur.
- Paste her zaman "kopyalandığı andaki son seçim konumuna" yapıştırıyor; klasik "yapıştır, sonra
  sürükleyerek konumlandır" akışı elle (Paste sonrası Move ile) yapılabiliyor ama tek adımda değil.
- Clipboard uygulama-içi bir alan (`SelectionCapture`); gerçek Windows panosu
  (`System.Windows.Clipboard`) kullanılmıyor, yani RugCAD dışına kopyala/yapıştır çalışmıyor.
- Alt/Shift/Ctrl basılıp bırakıldığında (fare hareket etmeden) marquee/renk anlık güncellenmiyor
  — sadece bir sonraki `MouseMove` event'inde yenileniyor. Küçük bir görsel gecikme, işlevi
  etkilemiyor.

### 4.5 Manuel test listesi (kullanıcı HENÜZ görsel olarak doğrulamadı — bir sonraki oturumda sun)

1. Boş alanda sürükle → beyaz kesikli dikdörtgen seçim.
2. Bir seçim varken Shift+sürükle (seçim dışında bir yerde) → yeşil önizleme, bırakınca iki
   bölge de seçili olmalı (birleşim).
3. Alt+sürükle (seçim dışında) → turuncu-kırmızı önizleme, bırakınca çakışan kısım seçimden
   çıkmalı.
4. Shift+Alt+sürükle → camgöbeği önizleme, bırakınca sadece kesişim kalmalı.
5. Seçili alanın İÇİNDE (gerçekten seçili bir pikselde) tıklayıp sürükle, HİÇBİR tuşa basmadan
   bırak → **Document'e HİÇBİR ŞEY yazılmamalı** (Ctrl+Z ile geri alınacak yeni bir undo adımı
   OLUŞMAMALI), içerik sadece yeni konumda görsel olarak durmalı.
6. Aynı yüzen içeriği TEKRAR tutup başka bir yere sürükle (yine tuşsuz bırak) → yine hiçbir şey
   desene yazılmamalı, önceki bıraktığın yerde de İZ KALMAMALI (bu, düzeltilen orijinal hata).
7. Şimdi Ctrl basılı tutarak sürükle/bırak → içerik SON konuma taşınmalı, kaynak (ilk, hiç
   dokunulmamış orijinal konum) arka plan rengine dönmeli, TEK Ctrl+Z ile tüm işlem (önceki
   tuşsuz sürüklemeler dahil) geri alınabilmeli (çünkü onlar hiç undo kaydı oluşturmadı).
8. Yüzen bir içerikken Edit → Duplicate Selection (Ctrl+J) çalıştır → o an durduğu konumda bir
   KOPYA desene işlenmeli, orijinal konumdaki içerik DE yerinde kalmalı (silinmemeli).
9. Yüzen bir içerikken Esc'e bas (Select None) → içerik son durduğu konumda KOPYA olarak
   desene işlenmiş olmalı (release=duplicate kuralı), seçim kalkmalı.
10. Copy (Ctrl+C) → Paste (Ctrl+V): içerik son seçim konumuna yeniden yapıştırılmalı.
11. Delete (yüzerken) → orijinal (hâlâ dokunulmamış) konumdaki içerik silinmeli, yüzen önizleme
    kaybolmalı.
12. Select All (Ctrl+A) / Select None (Esc) menüden ve kısayoldan çalışmalı.
13. L-şekilli bir seçim oluşturup (Shift ile iki dikdörtgen birleştirerek) İÇİNDE bir yere
    tıklayıp sürükle → sadece gerçekten seçili hücreler taşınmalı/kopyalanmalı, L'nin "boş"
    köşesindeki pikseller etkilenmemeli.
14. Boş alanda sürükle → beyaz kesikli dikdörtgen seçim.
15. Bir seçim varken Shift+sürükle (seçim dışında bir yerde) → yeşil önizleme, bırakınca iki
    bölge de seçili olmalı (birleşim).
16. Alt+sürükle (seçim dışında) → turuncu-kırmızı önizleme, bırakınca çakışan kısım seçimden
    çıkmalı.
17. Shift+Alt+sürükle → camgöbeği önizleme, bırakınca sadece kesişim kalmalı.

---

## 5. Önemli Teknik Notlar / Tuzaklar (ileride tekrar karşılaşmamak için)

1. **`LayoutDocument.Title="{Binding ...}"` ÇALIŞMAZ.** AvalonDock'un `LayoutDocument`'ı görsel/
   mantıksal ağacın parçası değil, bu yüzden DataContext miras almıyor ve Binding sessizce boş
   kalıyor. Başlığı `MainWindow.xaml.cs` içinde `MainDocument.Title = viewModel.DocumentTitle;`
   şeklinde ELLE atıyoruz (bkz. `ApplyDocument` metodu).
2. **Menu/MenuItem/ToolBar/Button için `Background="{DynamicResource ...}"` yetmez.** WPF'in
   varsayılan Aero2 teması bu kontrollerin görsel state'lerinin çoğunu görmezden geliyor. Tüm
   kontrol şablonları `Themes/ControlStyles.xaml`'de sıfırdan tanımlandı. Yeni bir WPF kontrolü
   (ör. ComboBox, ListBox, ScrollBar) eklenirse AYNI SORUNLA karşılaşılır — o kontrol için de
   `ControlStyles.xaml`'e özel bir `ControlTemplate` eklenmesi gerekir.
3. **AvalonDock'un kendi teması ayrı yönetiliyor.** `MainWindow.xaml.cs` → `ApplyTheme()` içinde
   `Dock.Theme = new Vs2013DarkTheme()/Vs2013LightTheme()` ile senkronize ediliyor. Sadece
   `Themes/Dark.xaml`/`Light.xaml`'i değiştirmek AvalonDock panellerini etkilemez.
4. **Pencere başlık çubuğu (title bar) da elle koyulaştırılıyor** — `WindowChromeTheming.cs`
   içinde DWM API'si (`DwmSetWindowAttribute`) ile. Bu native bir Windows 10 1809+/11 özelliği.
5. **DPI kavramı yanlış — "Warp density" / "Weft density" kullan.** Kullanıcı görsel DPI değil,
   dokuma sektöründeki **tarak (warp/reed density, X ekseni)** ve **atkı (weft density, Y
   ekseni)** kavramlarını istiyor. `DesignSurfaceViewModel.WarpDensity`/`WeftDensity` (int,
   varsayılan 32/23) bunun için var. İleride gerçek bir "Resize Design" dialogu ile kullanıcı
   tarafından ayarlanabilir hale getirilmeli (Texcelle'in Design→Resize dialogu örnek alınabilir,
   bkz. docs/02).
6. **Döküman başlığı formatı Texcelle'i birebir taklit ediyor:**
   `"{Name} ({FileType}) {W} x {H} ({WarpDensity}/{WeftDensity})"` — örn.
   `"Pattern Design 1 (RUGCAD) 60 x 60 (32/23)"`. `FileType` şu an sabit `"RUGCAD"` placeholder;
   gerçek dosya aç/kaydet eklenince gerçek uzantı (BMP/GIF/vb.) buraya yansıtılmalı.
7. **Build sırasında "dosya kilitli" hatası alırsan** (`MSB3027`/`MSB3021`), önce
   `taskkill //IM RugCAD.App.exe //F` çalıştır — muhtemelen önceki test çalıştırmasından kalan
   process .exe dosyasını kilitliyor. Bu ortamda birden fazla kez karşılaşıldı.
8. **`PaintStrokeCommand.AddPixel` pikseli ÇAĞRILDIĞI ANDA uygular** (Document.SetPixel
   çağırır), sadece "staged" tutmaz. Bu, pencil aracının canlı önizlemesi için bilinçli bir
   tasarım kararı. Yeni bir command türü yazarken bu davranışı taklit et veya bilerek farklı
   davran (shape tool'lar bu yüzden Document'e dokunmadan ayrı bir SkiaSharp preview kullanıyor
   — bkz. `DesignCanvas.xaml.cs` → `DrawShapePreview`).
9. **NU1701 paket uyarıları zararsız.** SkiaSharp.Views.WPF, AvalonDock ve tema paketleri
   .NET Framework hedefli sürümleriyle geri dönüşümlü çözülüyor (net8.0-windows7.0 tam eşleşme
   yok ama derleme/çalışma sorunsuz). Bu uyarıları görürsen normal, hata değil.

## 6. Şu Ana Kadar Tamamlanan MVP Özellikleri (Katman 1, docs/04 Bölüm 4)

- [x] Piksel tabanlı çizim tuvali (indexed-color palet)
- [x] Undo/Redo (komut tabanlı, çalışıyor, görsel de anlık yenileniyor)
- [x] Çizim araçları: Pencil, Eyedropper, Bucket/Fill, Line, Rectangle, Ellipse
- [x] Dockable panel sistemi (AvalonDock, Photoshop tarzı taşınabilir paneller)
- [x] Dark/Light tema (menü, toolbar, AvalonDock panelleri, pencere başlık çubuğu dahil)
- [x] Alt bilgi çubuğu (isim/boyut/warp-weft density + hover piksel bilgisi)
- [x] Palet kutularında renk numarası
- [x] Selection (seçim) aracı — tamamlandı, henüz kullanıcı tarafından görsel doğrulanmadı (bkz. Bölüm 4)
- [ ] Zoom/Pan (henüz yok, sabit 12px/piksel)
- [ ] Dosya aç/kaydet (`.rugcad` formatı) + PNG/BMP içe-dışa aktarım (henüz yok)
- [ ] Repeat (rapor) önizleme (henüz yok)
- [ ] Temel transform: mirror/rotate/scale (henüz yok)
- [ ] Araç ayarları (kalem kalınlığı, dolgu/çerçeve modu, bucket toleransı vb. — henüz yok)

## 7. Build / Test / Run Komutları

```bash
# Proje kökünden (D:\YLDRMHASAN\PROGRAMLAR\rugcad):
dotnet build src/RugCAD.App/RugCAD.App.csproj      # WPF uygulamasını derle
dotnet test tests/RugCAD.Core.Tests/RugCAD.Core.Tests.csproj  # Core birim testlerini çalıştır

# Çalıştırmadan önce eski process varsa kapat (bkz. Bölüm 5, madde 7):
taskkill //IM RugCAD.App.exe //F

# Çalıştır:
dotnet run --project src/RugCAD.App/RugCAD.App.csproj
# veya derlenmiş exe'yi doğrudan:
"src/RugCAD.App/bin/Debug/net8.0-windows/RugCAD.App.exe"
```

## 8. Önerilen Sıradaki Adımlar (Selection tamamlandıktan sonra)

Kullanıcı ile üzerinde konuşulan öncelik sırası (henüz kesin karar yok, sorulabilir):
1. ~~Selection aracını bitir~~ — TAMAMLANDI (Bölüm 4).
2. ~~Mevcut araçlara ayar paneli~~ — TAMAMLANDI (Bölüm 11).
3. ~~Zoom/Pan~~ — TAMAMLANDI (Bölüm 10).
4. Dosya aç/kaydet (`.rugcad` formatı) + PNG/BMP içe-dışa aktarım.
5. Repeat (rapor) önizleme, temel transform (mirror/rotate/scale).

Herhangi birine geçmeden önce kullanıcıya hangisini istediğini sormak iyi bir alışkanlık oldu
bu projede (AskUserQuestion ile) — kullanıcı net tercihler belirtiyor ve bunları hızlıca
onaylıyor.

## 10. Zoom/Pan + Şekil Araçları Kısıtlamaları (Ctrl=merkez, Shift=kare/daire) — TAMAMLANDI

### 10.1 Zoom/Pan
- `DesignSurfaceViewModel`: `ZoomLevel` (double, 0.25–16.0 arası clamp'li), `ZoomIn/ZoomOut/
  ResetZoom/SetZoom`, `ZoomPercentText` (`"100%"` gibi), `ZoomInCommand/ZoomOutCommand/
  ResetZoomCommand`.
- `DesignCanvas.xaml.cs`: `PixelSize` artık sabit değil, `BasePixelSize (12) * ZoomLevel`
  hesaplayan bir property; `ZoomLevel` her değiştiğinde `SkElement.Width/Height` yeniden
  hesaplanıyor (`UpdateCanvasSize`) ve yeniden çiziliyor.
- **Ctrl+fare tekerleği** → yakınlaştır/uzaklaştır (`OnPreviewMouseWheel`, ScrollViewer'ın normal
  kaydırmasını Ctrl basılıyken engelliyor).
- **Orta fare tuşu + sürükle** → pan (`OnPanMouseDown/OnMouseMove/OnPanMouseUp`, ScrollViewer'ın
  `HorizontalOffset/VerticalOffset`'ini elle ayarlıyor).
- MainWindow'da Zoom -/Zoom %/Zoom + /100% toolbar butonları + View menüsünde karşılıkları.
- **Not:** "Fit to Window" (pencereye sığdır) HENÜZ YOK — istenirse eklenmeli (viewport
  boyutunu `Scroller.ViewportWidth/Height`'tan okuyup gereken zoom'u hesaplamak gerekir).

### 10.2 Rectangle/Ellipse/Selection için Ctrl=merkezden, Shift=fiziksel kare/daire — NİHAİ KURAL

Kullanıcı isteği birkaç mesajda netleşti (özet, "Photoshop gibi düşün" talimatıyla):
"mouse1 basılıyken Ctrl merkezden, Shift kare/daire yapsın; Shift AYRICA mevcut seçim varsa
ekleme (Add) anlamına da gelsin — ikisi ÇELİŞMİYOR, aynı anda ikisi de geçerli (tıpkı
Photoshop'ta Shift+sürüklemenin hem eklemesi hem kareye kısıtlaması gibi)."

**Nihai davranış (hem Rectangle/Ellipse hem Selection'da TUTARLI):**
- **Ctrl** → şekil `_dragStart` merkez kabul edilip simetrik büyütülür (`centered`).
- **Shift** → şekil **fiziksel** kare/daireye kısıtlanır (`constrainSquare`) — piksel sayısı
  eşitliği DEĞİL, `WeftDensity/WarpDensity` oranına göre düzeltilmiş gerçek kare/daire (tarak/
  atkı yoğunluğu farklı olabildiği için). Bu oran ileride yoğunluklar değişse bile otomatik
  doğru çalışır.
- **Selection'a özel:** Shift AYRICA (yukarıdakiyle ÇAKIŞMADAN, aynı anda) mevcut bir seçim
  varsa Add (birleşim) demek; Alt Subtract (fark); Shift+Alt Intersect (kesişim); hiçbiri yoksa
  Replace. Yani Shift+sürükle hem şekli kareye kısıtlar HEM de mevcut seçime ekler — Photoshop'un
  gerçek davranışı budur, ikisi arasında seçim yapmaya gerek yok.
- **Mod seçimi (Move mu, yeni seçim mi) SADECE tıklama anındaki (`mouse1` basılma anı) Shift/Alt
  durumuna göre belirleniyor** (`wantsNewMarquee` — `OnMouseLeftButtonDown`): tıklama anında
  Shift veya Alt basılıysa (tıklanan piksel seçili olsa bile) her zaman YENİ SEÇİM moduna
  girilir. Basılı DEĞİLSE ve piksel seçiliyse Move moduna girilir. **Bu noktadan sonra** (Move
  modu zaten kilitlenmişken) sürükleme sırasında/bırakırken Ctrl (kes/taşı) ve Shift (tuval
  dışına izin) kendi AYRI anlamlarını korur (bkz. 4.2/4.2b) — çakışma yok çünkü mod zaten
  tıklama anında belirlendi, sonradan değişmiyor.
- `GetCombineModeFromModifiers()` VE `GetConstrainedShapeCorners()`/`GetConstrainedSelectionCorners()`
  birbirinden BAĞIMSIZ iki ayrı hesaplama — biri "birleşim modu", diğeri "şekil geometrisi" —
  aynı Shift tuşundan farklı iki bilgi okuyorlar, çelişmiyorlar.

**ÖNEMLİ — bu konuda kullanıcı 3 kez fikir değiştirdi/netleştirdi, en son bu kural GEÇERLİ:**
1. İlk hali: Shift/Alt ikisi de "her zaman yeni seçim" tetikliyordu, Ctrl sadece merkez.
2. Sonra: "Add otomatik olsun (seçim varsa modifier'sız), Shift/Ctrl tamamen şekle ayrılsın"
   dendi ve öyle yapıldı.
3. **Son ve GEÇERLİ hali (yukarıdaki):** Klasik Photoshop'a geri dönüldü — Shift=Add+Kare
   (birlikte), Alt=Subtract, Shift+Alt=Intersect, modifiersız=Replace. Ctrl=merkez (şekil,
   Selection dahil tüm araçlarda tutarlı).

## 9. Layer (Katman) Sistemi — BİLİNÇLİ OLARAK ERTELENDİ

Kullanıcı katman/layer mantığını istiyor ama **AskUserQuestion ile "ilerde ekleyelim"i (Recommended
seçenek) onayladı** — şimdi ekleme kararı verilmedi. **Bunu kullanıcıya sormadan sessizce
uygulamaya başlama; ne zaman gündeme gelirse bu bölümü güncelle ve yeni bir onay iste.**

Neden erteleme önerildi: mevcut `RugCAD.Core.Models.DesignDocument` tek katmanlı, düz bir piksel
ızgarası (`byte[]` + tek `Palette`). Layer eklemek en az şunları etkiler:
- **Core model**: `DesignDocument` yerine bir katman yığını (`Layer[]`, her biri kendi piksel
  ızgarası + opacity + blend mode + visible/locked bayrakları) gerekir.
- **Undo/Redo**: `PaintStrokeCommand`/`IDesignCommand` şu an "hangi Document" bilgisini üstü kapalı
  alıyor — katman-farkında hale getirilmesi (hangi katmana yazıldığı da undo kaydına girmeli) gerekir.
- **Render**: `DesignCanvas.OnPaintSurface` şu an tek katmanı doğrudan çiziyor; katmanlarla
  compositing (alttan üste, opacity/blend mode uygulayarak) gerekir — SkiaSharp bunu destekler
  (`SKPaint.BlendMode`, layer'lar için `SaveLayer`), ama mevcut basit döngü yeniden yazılmalı.
  Ayrıca "Active Pattern" panelindeki placeholder muhtemelen gerçek bir "Layers" paneline
  dönüşecek.
- **Seçim sistemi**: Bölüm 4'teki `_selectionMask` şu an tek bir dokümanın koordinat sistemine
  göre; çok katmanlı bir yapıda "hangi katmana uygulanıyor" sorusu netleşmeli (aktif katmana mı,
  tüm katmanlara mı).
- **Dosya formatı**: Henüz dosya kaydetme/yükleme (`.rugcad`) implemente edilmedi (bkz. Bölüm 8,
  madde 4) — layer'lı bir format tasarımını dosya formatını ilk kez yazarken baştan düşünmek,
  sonradan format migrasyonu yapmaktan çok daha ucuz. **Bu yüzden layer kararını, dosya
  aç/kaydet özelliğine başlamadan HEMEN ÖNCE gündeme getirmek stratejik olarak en doğrusu** —
  o noktada hem format hem de model tasarımı birlikte, tutarlı şekilde yapılabilir.

Kısacası: layer'ı Bölüm 8'in 4. maddesiyle (dosya aç/kaydet) birlikte, ondan hemen önce bir karar
noktası olarak kullanıcıya tekrar sor.

## 11. Elips Rasterizasyon Düzeltmesi + Araç Ayarları Paneli — TAMAMLANDI

### 11.1 Elips çiziminde eşitsiz kalınlık hatası (kullanıcı ekran görüntüsüyle buldu)

`Rasterizer.EllipseOutline` başlangıçta **açı bazlı örnekleme** ile yazılmıştı ("basit ama görsel
olarak ayırt edilemez" varsayımıyla — YANLIŞ çıktı). Düz (yatay/dikey eksene yakın) bölgelerde
noktalar sıklaşıp bindirmeli/kalın görünüyordu, dik (45°'ye yakın) bölgelerde seyrekleşip
inceliyordu. **Çözüm:** gerçek, klasik **midpoint ellipse algoritması** (iki bölgeli, 45°'de
eksen değiştiren, her adımda tam 1 piksel ilerleyen) ile değiştirildi — bu algoritma matematiksel
olarak HER YERDE tek piksel kalınlık garantiler, sample-yoğunluğu sorunu yapısal olarak ortadan
kalkar. `RugCAD.Core.Drawing.Rasterizer.EllipseOutline` — kod + geniş açıklama yorumu var.

Ayrıca aynı dosyaya `RectangleFilled`, `EllipseFilled` (satır-bazlı tarama ile dolgu) ve
`SquareBrush` (pencil fırçası için, bkz. 11.2) eklendi. Toplam Core testi artık **14** (5 yeni:
`RasterizerFillTests.cs`).

### 11.2 Araç ayarları paneli (contextual, MainWindow'da aktif araca göre gösterilen ToolBar'lar)

- **Pencil → Size (1-20):** `DesignSurfaceViewModel.PencilSize`; `PaintPixel(x,y)` artık tek
  piksel yerine `Rasterizer.SquareBrush(x,y,PencilSize)` ile bir kare fırça boyuyor.
- **Rectangle/Ellipse → Filled:** `DesignSurfaceViewModel.ShapeFilled` (bool); `DesignCanvas.
  GetShapePoints` bu bayrağa göre Outline yerine Filled varyantını kullanıyor (hem önizlemede
  hem commit'te aynı kod yolu, tutarlı).
- **Bucket → Global Fill:** `DesignSurfaceViewModel.BucketGlobalFill` (bool) +
  `GetGlobalFillRegion(x,y)` (bağlantılı olmasına bakmaksızın DOKÜMANDAKİ TÜM aynı renkli
  pikselleri bulur). `DesignCanvas`'ta Bucket handler bu bayrağa göre `FloodFill.GetRegion`
  (bağlantılı) veya `GetGlobalFillRegion` (global) arasında seçim yapıyor.
- **UI:** `MainWindow.xaml`'de her araç için ayrı bir `ToolBar`, `Converters/
  DrawToolVisibilityConverter.cs` ile SADECE ilgili araç aktifken görünür hale getiriliyor
  (ConverterParameter'da `"Rectangle|Ellipse"` gibi pipe-ayraçlı liste destekleniyor).
- **Bilinen sınırlama:** `Slider` ve `CheckBox` kontrolleri `Themes/ControlStyles.xaml`'de
  ÖZEL ŞABLONLANMADI (Menu/ToolBar/Button gibi) — WPF'in varsayılan Aero2 temasıyla
  görüntüleniyorlar. Görsel olarak muhtemelen kabul edilebilir (bu küçük kontroller Menu kadar
  agresif şekilde DynamicResource'u görmezden gelmiyor) ama dark temada hafif "yabancı" durabilir
  — kullanıcı test edip rahatsız ediciyse bildirsin, o zaman bu ikisi için de özel şablon
  eklenir (bkz. Bölüm 5, madde 2 — aynı kalıp).

## 12. Şimdilik Light-Only + Maximized Başlangıç — KULLANICI KARARI

Kullanıcı: "şimdilik programı light olarak kodlayalım ve light olarak çalışsın, ilerde dark/light
uyumunu yaparız, bir de uygulama tam ekran olarak açılsın."

- `App.xaml.cs`: `OnStartup` artık Windows'un sistem temasını (registry'den `AppsUseLightTheme`)
  OTOMATİK ALGILAMIYOR — her zaman `ThemeManager.Apply(AppTheme.Light)` ile başlıyor.
  **`DetectSystemTheme()` metodu tamamen kaldırıldı** (dosyadan silindi, `Microsoft.Win32`
  using'i de gitti). View menüsündeki manuel Light/Dark Theme geçiş komutları HÂLÂ ÇALIŞIYOR
  (elle test etmek için), sadece açılışta otomatik karanlık moda geçme kaldırıldı.
- `MainWindow.xaml`: `WindowState="Maximized"` eklendi — uygulama artık her zaman tam ekran açılır.
- **Bu, Bölüm 5 ve önceki bölümlerdeki dark tema altyapısını (ControlStyles.xaml, AvalonDock
  Vs2013Dark/LightTheme senkronizasyonu, DWM title bar koyulaştırma) SİLMEDİ/BOZMADI** — hepsi
  yerinde duruyor ve View menüsünden manuel olarak hâlâ test edilebilir. Sadece "hangi tema
  varsayılan" kararı değişti. İleride "dark/light uyumunu yapalım" denildiğinde muhtemelen
  Slider/CheckBox gibi henüz şablonlanmamış kontrollerin (bkz. 11.2) dark temada nasıl
  göründüğünü kontrol edip gerekirse şablon eklemek yeterli olacak — temel altyapı zaten hazır.

## 13. Araç Ayarları → Toolbar'dan Ayrı Bir Dockable Panele Taşındı

Kullanıcı isteği: "araç ayarları farklı pencere içinde (hepsini kapsıyacak, seçili olan aracın
özelliklerini gösterecek), Active Pattern gibi panel olsun." Bölüm 11.2'de toolbar satırları
olarak eklenen Pencil Size/Filled/Global Fill ayarları **kaldırılıp** yeni bir dockable panele
taşındı:

- **`Controls/ToolOptionsPanel.xaml(.cs)`** (yeni dosya): `DesignSurfaceViewModel`'e bağlı, tek
  bir panel — üstte aktif aracın adı (`{Binding CurrentTool}`), altında SADECE o araca ait
  ayarlar (`DrawToolVisibilityConverter` ile görünürlük kontrolü, Bölüm 11.2'dekiyle aynı
  converter/mantık, sadece toolbar yerine dikey bir `StackPanel` içinde). Hiçbir ayarı olmayan
  araçlar için (Selection/Eyedropper/Line) "This tool has no additional options yet." mesajı.
- **`MainWindow.xaml`**: Bu panel, "Active Pattern" ile AYNI `LayoutAnchorablePane` içine
  `Title="Tool Options"` bir `LayoutAnchorable` olarak eklendi — AvalonDock aynı pane içindeki
  birden fazla anchorable'ı otomatik olarak SEKME (tab) haline getiriyor, yani "Tool Options" ve
  "Active Pattern" şimdi aynı panelde iki sekme olarak yan yana duruyor (kullanıcının istediği
  "Active Pattern gibi panel" tam olarak bu). Eski toolbar satırları (`Visibility` bağlamalı üç
  `ToolBar`) `MainWindow.xaml`'den TAMAMEN SİLİNDİ; `Window.Resources`'taki
  `DrawToolVisibilityConverter` kaydı da (artık `ToolOptionsPanel`'in kendi
  `UserControl.Resources`'ında tanımlı olduğu için) kaldırıldı.
- Bağlı ViewModel property'leri (`PencilSize`, `ShapeFilled`, `BucketGlobalFill`) DEĞİŞMEDİ —
  sadece hangi XAML'in bunlara bağlandığı değişti, mantık aynı.

**GÜNCELLEME (Bölüm 14'e bakınız): `PencilSize`/`ShapeFilled` daha sonra tamamen kaldırılıp
araç-bazlı bağımsız property'lere bölündü.**

## 14. Elips Rasterizasyonu v3 (Running Cords) + Araç-Bazlı Bağımsız Ayarlar + Panel Kurtarma

### 14.1 Elips dış hattı — üçüncü ve son düzeltme + Texcelle'in "Running Cords" özelliği

Kullanıcı, Bölüm 11.1'deki "boundary-of-filled-region" düzeltmesinden SONRA bile ekran
görüntüsünde nadir bir 2-piksel-kalınlık noktası buldu. Aynı anda Texcelle'in kendi "Ellipse
Properties" diyaloğunun ekran görüntüsünü paylaştı: **Pen Size X/Y**, **Running Cords**
(checkbox), **Filled**, **Fill style** alanları var. İki daire karşılaştırması çok öğreticiydi:
Running Cords AÇIK olan daire uzun düz segmentli/bağlantılı görünüyordu, KAPALI olan daha ince/
köşegen-atlamalı. **Bu tam olarak benim önceki iki farklı elips algoritmamla (2-bölgeli midpoint
= bağlantılı görünüm, boundary-tracing = ince görünüm) örtüşüyordu** — yani "hangisi doğru"
sorusunun cevabı "ikisi de doğru, ikisi de farklı bir moda karşılık geliyor" oldu.

**Nihai çözüm — İKİ ayrı, her ikisi de kendi içinde MATEMATİKSEL OLARAK TUTARLI algoritma:**
- **`Rasterizer.EllipseOutline`** (Running Cords KAPALI): "boundary-of-filled-region" yöntemi —
  `IsInsideEllipse(x,y,...)` testi ile her hücrenin "içeride mi" olduğuna bakılıyor, İÇERİDE olup
  4-komşusundan en az biri DIŞARIDA olan hücreler sınır kabul ediliyor. Bu, Selection aracının
  maske sınırını çizdiğimiz YÖNTEMLE BİREBİR AYNI teknik (bkz. `DesignCanvas.
  DrawSelectionOutline`). Garanti: HİÇBİR ZAMAN 2 piksel kalınlık oluşamaz (per-hücre bağımsız
  bir predicate, adım adım ilerleyen bir algoritma değil — "dikiş noktası" diye bir şey yok).
- **`Rasterizer.EllipseOutlineConnected`** (Running Cords AÇIK, varsayılan): sütun-taraması
  (her x için üst/alt sınır) + satır-taraması (her y için sol/sağ sınır) birleşimi. Bu, HER
  ZAMAN 4-komşuluklu (edge-connected) kalmayı garanti eder — gerçek bir ipliğin/kordonun fiziksel
  olarak kesintisiz takip edebileceği bir yol, köşegen (corner-only) atlama YOK. Bu yüzden daha
  uzun düz segmentler + bazı köşelerde "ekstra" bir piksel görünümü oluşur (screenshot'taki sol
  daireyle eşleşiyor).
- **`Rasterizer.EllipseFilled`** her ikisiyle de aynı `IsInsideEllipse` testini paylaşıyor, yani
  outline HER ZAMAN filled'ın gerçek sınırı (asla 1 piksel kayık değil).
- **`Rasterizer.Dilate(points, sizeX, sizeY)`** (yeni, genel amaçlı): herhangi bir nokta
  kümesini (outline, line) her noktada bir `RectBrush` damgalayarak kalınlaştırır — Pen Size X/Y
  özelliğinin uygulanma şekli budur, şekil algoritmasından TAMAMEN AYRI bir kaygı (hangi şekil /
  ne kalınlık birbirinden bağımsız, Line/Rectangle/Ellipse'in hepsinde yeniden kullanılabilir).
- Core testleri artık **19** (4 yeni: `EllipseOutlineConnected` bağlantılılık testi, `Dilate`
  testleri, `EllipseOutline`/`EllipseFilled` sınır tutarlılığı).

### 14.2 Araç ayarları artık TAMAMEN bağımsız (kullanıcı isteği: "birinde değişince diğerleri değişmesin")

Eski tek `ShapeFilled`/`PencilSize` property'leri KALDIRILDI, yerine:
- `PencilSizeX`, `PencilSizeY` (Pencil — artık kare değil, dikdörtgen fırça; `Rasterizer.RectBrush`
  ile, `SquareBrush` kaldırıldı).
- `RectanglePenSizeX`, `RectanglePenSizeY`, `RectangleFilled` (Rectangle'a özel, Ellipse'i
  ETKİLEMEZ).
- `EllipsePenSizeX`, `EllipsePenSizeY`, `EllipseFilled`, `EllipseRunningCords` (Ellipse'e özel,
  Rectangle'ı ETKİLEMEZ; `EllipseRunningCords` varsayılan `true`).
- `Controls/ToolOptionsPanel.xaml` bu yeni property'lere göre yeniden yazıldı: Rectangle ve
  Ellipse artık ayrı `StackPanel` bölümleri (kendi Pen Size X/Y + kendi Filled checkbox'ı),
  Ellipse'te ayrıca "Running Cords" checkbox'ı (Filled iken devre dışı — `InverseBoolConverter`,
  yeni dosya, ile).
- `DesignCanvas.GetShapePoints`: Rectangle/Ellipse artık kendi Pen Size'ıyla `Rasterizer.Dilate`
  uyguluyor (Filled iken dilate atlanıyor, zaten dolu); Ellipse ayrıca `EllipseRunningCords`
  bayrağına göre `EllipseOutline` veya `EllipseOutlineConnected` seçiyor.

### 14.3 Toolbar araç grupları + View > Panels (kaybolan panel kurtarma)

- `MainWindow.xaml` araç toolbar'ına `Separator`'lar eklendi: **Selection** | **Pencil,
  Eyedropper, Bucket** | **Line, Rectangle, Ellipse** — üç mantıksal grup.
- **View → Panels** alt menüsü eklendi: "Tool Options", "Active Pattern", "Color Palette" —
  her biri ilgili `LayoutAnchorable`'ı `.Show()` + `.IsActive=true` ile öne getirir (gizlenmiş
  veya arkada kalmış bir paneli geri getirmenin basit yolu).
- **View → Panels → Reset Layout to Default**: üç paneli de orijinal pane'lerine
  (`LeftPane`/`RightPane`, artık `x:Name` verildi) geri ekleyip `DockWidth`'leri sıfırlıyor,
  sonra hepsini `.Show()` ediyor. **BU TAM BİR "KAYDET/GERİ YÜKLE" (disk'e layout XML yazma)
  DEĞİL** — sadece bilinen 3 panelin bilinen 2 pane'e manuel olarak geri yerleştirilmesi. Gerçek
  `XmlLayoutSerializer` tabanlı tam layout kaydetme/geri yükleme (kullanıcının "layout kaydetme
  gibi şeyler" isteğinin tam karşılığı) **AYRI, DAHA BÜYÜK BİR İŞ** — `LayoutSerializationCallback`
  ile içerik-kimliği eşleştirmesi gerektiriyor (bkz. AvalonDock dokümantasyonu), zaman/risk
  dengesi için ŞİMDİLİK YAPILMADI. Kullanıcı bunu gerçekten istiyorsa (uygulama kapanıp
  açıldığında panel düzeninin PERSİST etmesi) ayrı bir görev olarak ele alınmalı.

### 14.4 DÜZELTME: Running Cords AÇIK/KAPALI eşlemesi TERSTİ

Bölüm 14.1'de "Running Cords AÇIK = `EllipseOutlineConnected` (uzun düz segment), KAPALI =
`EllipseOutline` (ince/köşegen)" varsayımı **fiziksel iplik sürekliliği teorisine dayanıyordu ve
YANLIŞ ÇIKTI.** Kullanıcı iki referans ekran görüntüsü (teal/mavi = Running Cords AÇIK, pembe =
KAPALI) paylaştı ve gerçek eşleme TAM TERSİYMİŞ:

- **Running Cords AÇIK (varsayılan, `true`)** → `Rasterizer.EllipseOutline` (boundary-tracing,
  ince, köşegen atlamalı, daha pürüzsüz/smooth).
- **Running Cords KAPALI (`false`)** → `Rasterizer.EllipseOutlineConnected` (satır+sütun
  taraması, uzun düz segmentler, bazı köşelerde belirgin kalınlaşma).

`DesignCanvas.GetShapePoints`'teki ternary koşulu buna göre TERSİNE ÇEVRİLDİ. `DesignSurfaceViewModel.
EllipseRunningCords` property'sinin XML doc yorumu ve `ToolOptionsPanel.xaml`'deki tooltip de
güncellendi. **Ders: isim/teoriye güvenme, kullanıcının verdiği gerçek referans görüntüsüne göre
eşle.** Kullanıcı henüz bu düzeltmeyi görsel olarak doğrulamadı — bir sonraki test turunda
onaylatılmalı.

## 15. Line'a Running Cords + Toolbar Sürüklenebilirlik Regresyonu + File Grubu

### 15.1 Line aracına da Running Cords eklendi ("bindirmeli/bindirmesiz")

Kullanıcı: "line içinde bu ayar olmalı... biz meslekte bindirmeli/bindirmesiz olarak
söylüyoruz." Yani bu kavram sektörde bu isimle biliniyor; UI etiketi Texcelle'le tutarlılık için
İngilizce "Running Cords" kalsın ama tooltip'te Türkçe karşılığı da belirtildi.

- **`Rasterizer.ConnectDiagonalSteps(orderedPoints)`** (yeni, genel amaçlı): SIRALI bir nokta
  dizisini (Line'ın Bresenham çıktısı gibi zaten ardışık/komşu olan) alıp, iki ardışık nokta
  sadece KÖŞEGEN komşuysa (her ikisi de 1 birim farklıysa) aralarına köprü pikseli ekliyor —
  Line'ın "bindirmeli" modu bu. "Bindirmesiz" mod ise dokunulmamış düz Bresenham çıktısı
  (zaten köşegen adımlara izin veriyor, "Running Cords AÇIK" ile aynı mantık).
- `DesignSurfaceViewModel.LineRunningCords` (bool, varsayılan `true` = bindirmesiz, Ellipse'le
  tutarlı varsayılan). `DesignCanvas.GetShapePoints`'in Line dalı buna göre ya düz `Line`'ı ya
  da `ConnectDiagonalSteps(Line(...))`'ı döndürüyor.
- `ToolOptionsPanel.xaml`'e Line bölümü eklendi (tek bir "Running Cords" checkbox'ı, tooltip'te
  "bindirmesiz"/"bindirmeli" karşılıkları var). "Bu aracın ayarı yok" mesajından Line çıkarıldı.
- Core testleri artık **21** (2 yeni: `ConnectDiagonalSteps` bağlantılılık + değişmezlik testi).

### 15.2 REGRESYON DÜZELTMESİ: Toolbar'lar sürüklenemiyordu

Kullanıcı: "toolbar taşınabilir olmalı." Bu bir ÖZELLİK İSTEĞİ değil, **Bölüm 11/14'te dark tema
için `ToolBar`'ın `ControlTemplate`'ini özelleştirirken FARKINDA OLMADAN kırdığım bir
regresyondu.** Sebep: WPF'in `ToolBar.OnApplyTemplate`'i, şablonda **tam olarak
`"ToolBarThumb"` adında bir `Thumb`** arıyor ve onun `DragDelta` olayını `ToolBarTray`'in
sürükle-yeniden-sırala/farklı-satıra-taşı mantığına bağlıyor — bu isim yoksa `ToolBarTray`
içindeki HİÇBİR `ToolBar` sürüklenemez hale geliyor (görünüşte çalışıyor gibi ama sessizce kırık).
`Themes/ControlStyles.xaml`'deki `ToolBar` şablonuna bu isimde bir `Thumb` (küçük 6 noktalı bir
"tutamaç" görseliyle, temaya uygun) geri eklendi. **Ders: WPF'in bir kontrolün şablonunu TAMAMEN
override ederken, o kontrolün code-behind'ının (`OnApplyTemplate`) belirli isimlerde
`GetTemplateChild` çağırdığı "sihirli" parçaları (magic template parts) kaybetmek çok kolay ve
sessiz bir hata sınıfı — ileride başka bir kontrolün şablonu değiştirilirse bu ihtimal
akılda tutulmalı.**

### 15.3 Ana toolbar'a "File" grubu eklendi (sadece New — Open/Save/Save As henüz YOK)

Kullanıcı "main toolbar içinde ... dosya aç kaydet farklı kaydet gibi şeyler olmalı" dedi, ama
bu özellikler (gerçek dosya formatı/kaydetme) henüz UYGULANMADI (bkz. Bölüm 8, yol haritası
madde 4). **Bilinçli karar: sahte/işlevsiz Open/Save/Save As butonu EKLENMEDİ** — sadece gerçekten
çalışan `New` (`OnNewDesign`), Undo, Redo tek bir "File" grubunda toolbar'ın en başına kondu.
Open/Save/Save As butonları, o özellik gerçekten uygulandığında eklenecek. **Kullanıcıya bu
sırayı (önce dosya formatı/kaydetme özelliğini mi yapalım) sormak gerekebilir** — bkz. Bölüm 8.

## 16. Running Cords — NİHAİ/OTORİTE TANIM (birkaç kez tersine döndü, ARTIK SAPMA)

Bu özellik üzerinde ardışık birkaç mesajda YANLIŞ varsayımlarla ileri geri gidildi (bkz. Bölüm
14.1 ve 14.4 — İKİSİ DE ARTIK GEÇERSİZ). Kullanıcının en son, mekanik olarak kesin tarifi:

> "running cords dediğim, biz 1 pixel çiziyoruz ya, bunu sanki 1.5 pixelmiş gibi çizecek ve bu
> sadece 1 pixel iken olacak."

**Bu TEK CÜMLE artık otorite kaynağıdır, ekran görüntüsü yorumlamaya güvenme:**

- **Running Cords AÇIK**: 1 piksellik kalem SANKİ ~1.5 piksel gibi davranır — köşegen (diagonal)
  her adımda araya bir "köprü" pikseli eklenir, çizgi/dış hat asla tek bir köşe noktasından
  temas edip kesintiye uğramaz. Kod karşılığı: `Rasterizer.EllipseOutlineConnected` (Ellipse) /
  `Rasterizer.ConnectDiagonalSteps(line)` (Line) — yani eski adıyla "bindirmeli".
- **Running Cords KAPALI**: gerçek, ham 1 piksellik çizgi/dış hat — köşegen adımlara MÜSAADE
  edilir (köprü piksel YOK). Kod karşılığı: `Rasterizer.EllipseOutline` (Ellipse) /
  düz `Rasterizer.Line` çıktısı (Line) — "bindirmesiz".
- **KRİTİK KOŞUL: bu ayar SADECE kalem tam olarak 1×1 piksel iken anlamlıdır.** Ellipse/Rectangle
  için Pen Size X veya Y 1'den büyükse, `Rasterizer.Dilate` zaten dış hattı kalınlaştırıyor ve
  köşegen boşluklar zaten kapanmış oluyor — Running Cords'un ekleyecek bir şeyi kalmıyor. Bu
  yüzden `DesignCanvas.GetShapePoints`'te Ellipse dalı `EllipseRunningCords && penSizeX==1 &&
  penSizeY==1` şeklinde KOŞULLU uygulanıyor (Line'da böyle bir kalem kalınlığı kavramı yok, o
  yüzden Line'da bu koşul yok, her zaman uygulanıyor).

**Uygulanan kod (DesignCanvas.GetShapePoints, DrawTool.Ellipse ve DrawTool.Line dalları) ve
`DesignSurfaceViewModel.EllipseRunningCords`/`LineRunningCords` XML doc yorumları, `ToolOptionsPanel.xaml`
tooltip'leri bu NİHAİ tanıma göre güncellendi.** Kullanıcı bunu HENÜZ görsel olarak doğrulamadı
(bir önceki tur yine "hâlâ farklı" demişti, ama o zamanki kod hâlâ eski/yanlış varsayıma
dayanıyordu) — **bir sonraki oturumda MUTLAKA gerçek Texcelle karşılaştırmasıyla teyit ettir,
bu konuyu bir daha açmadan önce.**

## 17. Running Cords — Bölüm 16'nın "NİHAİ" tanımı GEÇERSİZ, eşleme TAM TERS ÇEVRİLDİ (kullanıcı talimatı, açıklama BEKLENİYOR)

Bölüm 16'daki "NİHAİ/OTORİTE TANIM" kullanıcı tarafından yine yanlış bulundu. Kullanıcının son
talimatı (birebir):

> "bak şuanda running cords ayarını tam tersi yap. sen benim dediğimi halen anlamadın sana
> daha sonra detaylı anlatıcam."

Yani: (1) mevcut eşleme yine yanlıştı, (2) kullanıcı ben (asistan) hâlâ ne demek istediğini
anlamadım diyor, (3) doğru açıklamayı **daha sonra, detaylı** verecek. Bu nedenle Bölüm 16'nın
mekanik tarifi (1.5 piksel, köprü pikseli vb.) artık **otorite kaynağı DEĞİL** — sadece geçmiş bir
yanlış varsayım olarak kayıtta kalıyor.

**Yapılan değişiklik:** `DesignCanvas.GetShapePoints`'te Line ve Ellipse dallarındaki
`RunningCords` üçlü operatörlerinin (ternary) iki tarafı birebir yer değiştirildi (mekanik flip,
teoriye dayanmadan):

- **Line**: `LineRunningCords ? Rasterizer.Line(...) : Rasterizer.ConnectDiagonalSteps(line)`
  (Bölüm 16'da tam tersiydi: açıkken köprülü, şimdi açıkken ham çizgi).
- **Ellipse** (sadece PenSizeX==1 && PenSizeY==1 iken anlamlı, koşul aynı kaldı):
  `EllipseRunningCords && ellipsePenIsOnePixel ? Rasterizer.EllipseOutline(...) :
  Rasterizer.EllipseOutlineConnected(...)` (Bölüm 16'da tam tersiydi).

Kod içindeki yorumlar da bu doğrultuda güncellendi: artık mekanik bir gerekçe yazmıyor, bunun
yerine "kullanıcının açık talimatıyla FLIP edildi, kullanıcının kendi detaylı açıklaması gelene
kadar bu eşlemeye bir daha teoriden veya ekran görüntüsünden dokunma" notu var.

**Doğrulama yapıldı:** `dotnet build` başarılı (0 hata), Core test paketi (21 test, tümü
`Rasterizer` üzerinde, `DesignCanvas`'a dokunmuyor) yine 21/21 yeşil — beklenen, çünkü flip sadece
hangi dalın seçildiğini değiştirdi, `Rasterizer` mantığının kendisini değiştirmedi. Uygulama
yeniden başlatıldı, çalışıyor.

**BİR SONRAKİ OTURUM İÇİN KRİTİK KURAL: Kullanıcı kendi detaylı açıklamasını verene kadar bu
özellik üzerinde TEORİ ÜRETME, ekran görüntüsünden yeniden yorumlama, ya da tekrar flip etme —
sadece kullanıcının yeni, detaylı tarifini birebir uygula.** Bölüm 14 (ilk yanlış varsayımlar),
Bölüm 16 ("NİHAİ" ama yine yanlış çıkan tanım) ve bu Bölüm 17 (kör flip) — üçü de bu konunun ne
kadar defalarca yanlış anlaşıldığının kaydı olarak korunuyor, silinmiyor.

## 18. Running Cords → "Pixel Cord" olarak yeniden adlandırıldı — kesin, kullanıcı-onaylı tanım (Bölüm 16/17 artık kapandı)

Kullanıcı sonunda kesin, mekanik ve netleştirilmiş tanımı verdi (birebir, İngilizce):

> "When Running Cords is disabled, draw the contour normally using the existing rasterization.
> Diagonal adjacency between consecutive cord cells is allowed. When Running Cords is enabled,
> the contour must become 4-connected. Two consecutive cord cells must never touch only at a
> corner. Whenever the normal rasterizer makes a diagonal step from (x, y) to (x+1, y+1),
> (x+1, y-1), etc., insert one additional orthogonal bridge cell so the path continues through
> shared edges. Do not replace or resize the original contour; only add the required bridge
> cells." — ayrıca özelliği "Pixel Cord" olarak yeniden adlandırmayı önerdi ("hatta bunu
> pixelcord olarak isimlendirsek daha doğru olur gibi"), bu da uygulandı.

**Bu artık OTORİTE TANIMDIR ve Bölüm 16/17'deki tüm önceki (yanlış) varsayımların, flip'lerin
yerini alır.** Özet:

- **Pixel Cord KAPALI** (varsayılan davranışın "off" hali): normal rasterizer çıktısı aynen
  kullanılır — `Rasterizer.Line` (Line) / `Rasterizer.EllipseOutline` (Ellipse, sadece
  PenSize 1×1 iken anlamlı). Köşegen (diagonal) komşuluk serbest.
- **Pixel Cord AÇIK**: kontur 4-bağlantılı hale getirilir — orijinal rasterizer'ın attığı her
  köşegen adımda (ör. (x,y)→(x+1,y+1)) araya tam olarak bir dik (orthogonal) köprü hücresi
  eklenir, böylece art arda gelen iki kontur hücresi ASLA sadece köşeden temas etmez, her zaman
  bir kenarı paylaşır. Orijinal kontur DEĞİŞTİRİLMEZ/yeniden boyutlandırılmaz, sadece gerekli
  köprü hücreleri eklenir. Kod karşılığı: `Rasterizer.ConnectDiagonalSteps(line)` (Line) /
  `Rasterizer.EllipseOutlineConnected` (Ellipse) — bu fonksiyonlar zaten tam olarak bu işi
  yapıyordu, sadece hangi tarafın "açık" hangi tarafın "kapalı" olduğu eşlemesi düzeltildi.
- Ellipse için koşul aynı kalıyor: sadece `EllipseFilled == false` VE kalem tam 1×1 iken anlamlı
  (kalem kalınlaştırılırsa `Dilate` zaten köşegen boşlukları kapatıyor).

**Yapılan değişiklikler:**
- `DesignCanvas.GetShapePoints` — Line ve Ellipse dallarındaki ternary'ler bu tanıma göre
  düzeltildi (Bölüm 17'nin kör flip'i buradaki gerçek anlamla aynı yöndeydi ama isimlendirme/
  yorumlar eksikti; şimdi kod hem doğru hem doğru şekilde belgelenmiş durumda).
- `DesignSurfaceViewModel` — `EllipseRunningCords`/`LineRunningCords` property'leri
  `EllipsePixelCord`/`LinePixelCord` olarak yeniden adlandırıldı (alan adları `_ellipsePixelCord`/
  `_linePixelCord`), XML doc yorumları yeni tanıma göre yazıldı.
- `ToolOptionsPanel.xaml` — checkbox metinleri "Running Cords" → "Pixel Cord", tooltip'ler yeni
  tanıma göre güncellendi, binding'ler yeni property adlarına taşındı.
- Build: 0 hata. Core testleri: 21/21 yeşil (beklenen — `Rasterizer` mantığı değişmedi, sadece
  `DesignCanvas`'ın hangi fonksiyonu çağırdığı ve isimlendirme değişti). Uygulama yeniden
  başlatıldı, çalışıyor.

**Bu konu artık KAPALI.** Bölüm 14/16/17 geçmiş yanlış anlaşılmaların kaydı olarak duruyor ama
bir daha bu tanımı sorgulamaya/yeniden yorumlamaya gerek yok — kullanıcı kesin, mekanik ifadeyle
onayladı.

## 19. Pixel Cord — Bölüm 18'in eşlemesi TERSİNE ÇEVRİLDİ (kullanıcı talimatı)

Kullanıcı, Bölüm 18'in kesin/otorite ilan edilen eşlemesinin ardından şu talimatı verdi (birebir):

> "Tersine al ve şunu uygula: 'Pixel Cords ON = diagonal cord transitions are not allowed;
> convert every diagonal transition into an overlapping orthogonal two-step transition by adding
> one bridge cord.'"

Not: bu mekanik tarif kelime kelime Bölüm 18'deki "ON = 4-connected/bridge" tanımıyla aynı şeyi
söylüyor — ama kullanıcı açıkça "tersine al" (reverse it) dedi. Yani kullanıcının UYGULAMADA
GÖRDÜĞÜ davranış, Bölüm 18'in koddaki eşlemesine göre BEKLENENİN TERSİYMİŞ (muhtemelen görsel
sonuç, "ON" ile "OFF" checkbox'ının kullanıcı beklentisiyle ters çalışıyormuş gibi görünmüş).
Talimat literal olarak uygulandı: **koddaki ON/OFF dalları birbiriyle yer değiştirildi**, tarifin
kendisi (bridge/4-connected fonksiyonlarının NE yaptığı) değişmedi.

**Yeni (şu anki) eşleme — Bölüm 18'in TAM TERSİ:**
- **Pixel Cord AÇIK (ON)**: düz/ham rasterizer çıktısı — `Rasterizer.Line` (Line) /
  `Rasterizer.EllipseOutline` (Ellipse, sadece 1×1 kalemde anlamlı). Köşegen komşuluk serbest.
- **Pixel Cord KAPALI (OFF)**: kontur 4-bağlantılı hale getirilir, her köşegen geçiş bir dik
  köprü hücresiyle iki adıma bölünür. Kod: `Rasterizer.ConnectDiagonalSteps` (Line) /
  `Rasterizer.EllipseOutlineConnected` (Ellipse).

Yani Bölüm 18'e göre ON ve OFF dalları birebir yer değiştirdi; `Rasterizer` fonksiyonlarının
kendisi (ne yaptıkları) hiç değişmedi, sadece `DesignCanvas.GetShapePoints`'teki hangi dalın
`LinePixelCord`/`EllipsePixelCord` `true`/`false` olduğunda seçildiği değişti.

**Doğrulama:** build 0 hata, Core testleri 21/21 yeşil (beklenen, `Rasterizer` mantığı
değişmedi), uygulama yeniden başlatıldı ve çalışıyor.

**Not düşülüyor ama tekrar açılmayacak:** Bölüm 18'in mekanik tarifiyle bu son talimatın mekanik
tarifi kelimesi kelimesine aynı olmasına rağmen kullanıcı "tersine al" dedi — bu, iki olası
açıklamadan biri olabilir: (a) Bölüm 18'in kodu, tarifi doğru uygulamamıştı (bir yerde
isim/checkbox ters bağlanmıştı), (b) kullanıcının UI'da gördüğü "ON" durumu, ViewModel'deki
gerçek `true` değeriyle ters yönde bir checkbox/binding sorunundan kaynaklanıyor olabilir. Bu
oturumda bu ihtimaller araştırılmadı — sadece literal talimat uygulandı. **Eğer kullanıcı bir
sonraki turda yine "ters" derse, teoriyle uğraşmadan önce ÖNCE `ToolOptionsPanel.xaml`'daki
checkbox binding'lerini ve `IsChecked` değerinin gerçekten `LinePixelCord`/`EllipsePixelCord`
property'sinin canlı değerini yansıtıp yansıtmadığını (ör. XAML'de tersine çevrilmiş bir
converter var mı) kontrol et — bu, tekrar tekrar "ters çık" şikayetinin kök nedeni olabilir.**

## 20. Pixel Cord — kesin ASCII örnekle doğrulandı: Bölüm 19'un tersine çevirmesi YANLIŞTI, Bölüm 18'in eşlemesi GERİ GETİRİLDİ

Kullanıcı, Bölüm 19'daki tersine çevirmeden sonra somut bir ASCII örnekle nihai açıklamayı verdi
(birebir):

> "Şu durum normal çizim: `X .` / `. X` — Bu sadece çapraz temas eder. Pixel cords açıkken şu
> olmalı: `X X` / `. X` veya `X .` / `X X`. Yani diagonal adım 2 ortogonal adıma çevrilmeli."

Bu, **Bölüm 18'deki tanımla birebir aynı** (ON = köşegen adımı bir köprü hücresiyle iki ortogonal
adıma çevir / 4-bağlantılı yap; OFF = düz köşegen temas serbest). Yani **Bölüm 19'un "tersine al"
talimatıyla yaptığım flip yanlış çıktı** — kullanıcının o zamanki "ters" algısının kökeni her ne
olursa olsun (checkbox/binding sorunu değil, muhtemelen o anki görsel karşılaştırmanın yanlış
yorumlanması), doğru mekanik tanım her zaman Bölüm 18'deki gibiymiş.

**Yapılan değişiklik:** `DesignCanvas.GetShapePoints`'teki Line ve Ellipse dallarındaki ternary'ler
Bölüm 18'deki haline (ON=bridge/connected, OFF=düz) GERİ ÇEVRİLDİ. Yorumlar bu ASCII örneğe atıfla
güncellendi.

**Şu anki (GEÇERLİ) eşleme:**
- **Pixel Cord AÇIK (ON)**: köşegen adım iki ortogonal adıma bölünür, bir köprü hücresi eklenir —
  `X X` / `. X` (veya eşdeğeri). Kod: `Rasterizer.ConnectDiagonalSteps` (Line) /
  `Rasterizer.EllipseOutlineConnected` (Ellipse, sadece 1×1 kalemde anlamlı).
- **Pixel Cord KAPALI (OFF)**: düz rasterizer çıktısı, köşegen temas serbest — `X .` / `. X`.
  Kod: `Rasterizer.Line` (Line) / `Rasterizer.EllipseOutline` (Ellipse).

**Doğrulama:** build 0 hata, Core testleri 21/21 yeşil, uygulama yeniden başlatıldı ve çalışıyor.

**Bölüm 19 artık geçersiz/yanlış bir ara adım olarak kayıtta kalıyor — bir daha o yöne
dönülmeyecek.** Bu konu artık gerçekten KAPALI: tanım hem mekanik ifadeyle hem somut ASCII
örnekle iki kez teyit edildi ve ikisi de aynı sonuca (Bölüm 18/20 eşlemesi) işaret ediyor.

## 21. Pixel Cord — Line doğru ama Ellipse ASCII tanıma uymuyordu: EllipseOutlineConnected'ın ALGORİTMASI baştan yazıldı

Bölüm 20'den sonra kullanıcı: "bak line tamam çalışıyor ancak ellipse dediğim şekilde değil"
dedi. Kök neden ON/OFF eşlemesi değildi (o doğruydu) — **`Rasterizer.EllipseOutlineConnected`'ın
kendi algoritması** hiçbir zaman Line'ın `ConnectDiagonalSteps`'iyle aynı mantığı uygulamıyordu.
Eski algoritma (Bölüm 14'ten kalma) tamamen farklı bir yöntemdi: ideal elips eğrisi üzerinde
bağımsız bir "column scan + row scan" (her x için üst/alt sınır, her y için sol/sağ sınır)
yapıp ikisinin birleşimini alıyordu — bu, `EllipseOutline`'ın (OFF) ürettiği gerçek/kesin sınırla
BAĞLANTISIZ ayrı bir eğri yaklaşıklamasıydı, dolayısıyla kullanıcının verdiği somut örnekle
("X ." / ". X" → "X X" / ". X") birebir eşleşmiyordu; kendi içinde tutarlı bir "4-bağlantılı"
şekil üretiyordu ama `EllipseOutline`'ın köşegen adımlarını birebir köprüleyen bir şey değildi.

**Düzeltme:** `EllipseOutlineConnected` artık `EllipseOutline`'ın (OFF modu) ürettiği tam nokta
kümesini temel alıyor ve Line'daki `ConnectDiagonalSteps` ile AYNI kuralı (köşegen komşu iki
sınır hücresi arasına bir dik köprü hücresi ekle) uyguluyor — tek fark, Line'ın noktaları zaten
sıralı/yürüme sırasında geldiği için ardışık çiftlere bakabilirken, elips sınırı sırasız bir küme
olduğundan her nokta için 4 köşegen komşusuna bakan bir versiyon yazıldı (`(1,1) (1,-1) (-1,1)
(-1,-1)` yönleri). İki olası köprü hücresinden (`(x+dx, y)` veya `(x, y+dy)`) hangisinin
ekleneceğine, elipsin GERÇEK sınırının dışında kalanı seçerek karar veriliyor — böylece eklenen
köprü her zaman dışa doğru bir "pad" oluyor, elipsin içine doğru büzülme olmuyor. Orijinal
`EllipseOutline` noktalarının hiçbiri silinmiyor/taşınmıyor, sadece köprü hücreleri ekleniyor
(kullanıcının "do not replace or resize the original contour" şartı).

**Test güncellemesi:** eski `EllipseOutlineConnected_HasAtLeastOnePointPerColumnAndRow` testi,
artık geçerli olmayan eski algoritmanın (column+row scan) bir yan ürünüydü — `EllipseOutline`'ın
kendisi bile en dıştaki satır/sütunda nokta bulundurmayabiliyor (bu, ızgara/merkez hizalamasının
matematiksel bir sonucu, regresyon değil), yani bu invariant yeni tanımla hiç ilgili değildi ve
kaldırıldı. Yerine, YENİ tanımı doğrudan sınayan iki test eklendi:
- `EllipseOutlineConnected_IsASupersetOfEllipseOutline` — ON modu, OFF modunun tüm noktalarını
  içermeli (hiçbir orijinal sınır hücresi kaybolmamalı).
- `EllipseOutlineConnected_HasNoCornerOnlyDiagonalTouches` — ON modundaki hiçbir köşegen komşu
  çift, aralarında en az bir dik köprü hücresi olmadan var olamaz (yani "X ." / ". X" durumu ON
  modunda asla kalmaz).

**Doğrulama:** Core testleri artık 22/22 yeşil (21 eskiden + 2 yeni − 1 kaldırılan). Build 0 hata.
Uygulama yeniden başlatıldı, çalışıyor.

**Sonuç: Line VE Ellipse artık gerçekten aynı mekanik tanımı (Bölüm 20'nin ASCII örneği)
uyguluyor — daha önce sadece Line doğruydu çünkü `ConnectDiagonalSteps` zaten bu mantıkla
yazılmıştı, Ellipse ise adı benzer ama özünde farklı bir algoritma kullanıyordu. Bu konu artık
algoritma düzeyinde de tutarlı ve KAPALI.**

## 22. Paper Format (Warp/Weft) editable panel eklendi + kanvas artık gerçek piksel en/boy oranını yansıtıyor (Shift-kare artık gerçekten daire görünüyor)

Kullanıcı Bölüm 21'deki ekran görüntüsünde (Pixel Cord açıkken düzgün 4-bağlantılı bir elips
görülüyor) şunu istedi: "şimdi paper format(kalite warp,weft) toggler ekle ve shift ile kare hale
getirdiğimiz çizimler kullanıcının gördüğü şekilde tam daire olucak."

**Kök neden analizi:** `ApplyShapeModifiers`'daki Shift-kare/daire matematiği zaten
`WarpDensity`/`WeftDensity` oranını kullanarak GERÇEK/fiziksel kare için doğru hücre sayısını
hesaplıyordu (bkz. Bölüm önceki kod). Ama `DesignCanvas`'ın render katmanı her hücreyi ekranda
her zaman KARE (`PixelSize x PixelSize`, ikisi de aynı `BasePixelSize * Zoom`) çiziyordu — yani
alttaki hücre ızgarası fiziksel olarak doğru olsa bile, ekranda hücre kare çizildiği için sonuç
kullanıcıya elips gibi görünüyordu (tam da ekran görüntüsündeki gibi). Ayrıca `WarpDensity`/
`WeftDensity` hiçbir UI'dan değiştirilemiyordu (`init`-only property'lerdi, sadece kurucuda
sabit 32/23 varsayılanları vardı).

**Yapılan değişiklikler:**

1. **`DesignSurfaceViewModel`**: `WarpDensity`/`WeftDensity` artık gerçek settable property
   (1-999 aralığına clamp'lenmiş), değiştiklerinde `PropertyChanged` fırlatıyorlar (kendi adları
   + `DocumentTitle` + `StatusInfoText`). `DocumentTitle` artık kurucuda bir kere hesaplanan sabit
   bir string değil, canlı (`get`-only computed) bir property — kullanıcı Warp/Weft'i
   değiştirdikçe güncel kalıyor.
2. **`DesignCanvas.xaml.cs`**: tek bir kare `PixelSize` yerine `PixelWidth` (eskisiyle aynı,
   referans boyut) ve `PixelHeight` (ondan `WarpDensity/WeftDensity` oranıyla türetilen, KARE
   OLMAYAN gerçek en-boy oranlı yükseklik) eklendi. Dosyadaki HER `PixelSize` kullanımı (ızgara
   çizimi, boyama önizlemesi, seçim overlay'i/marquee/outline, floating capture, kanvas
   Width/Height, fare→hücre koordinat dönüşümü) `PixelWidth`/`PixelHeight` ayrımına taşındı.
   Matematik: `PixelHeight = PixelWidth * (WarpDensity / WeftDensity)` — bu, `ApplyShapeModifiers`
   içindeki `ratio = WeftDensity/WarpDensity` (hücre-sayısı oranı) ile TAM TERS orantılı olacak
   şekilde seçildi ki fiziksel olarak kare olan bir şekil (`cellsY = cellsX * ratio`) ekranda da
   `cellsX*PixelWidth == cellsY*PixelHeight` yani gerçekten kare/daire GÖRÜNSÜN.
   `OnViewModelPropertyChanged` artık `WarpDensity`/`WeftDensity` değişimini de dinleyip kanvası
   yeniden boyutlandırıp yeniden çiziyor.
3. **Yeni dockable panel — "Paper Format"**: `Controls/PaperFormatPanel.xaml(.cs)`, "Tool
   Options"/"Active Pattern" ile aynı sol panelde, `WarpDensity`/`WeftDensity`'ye iki
   Slider+TextBox (1-200 aralığı, canlı iki yönlü binding) ile bağlı. `MainWindow.xaml`'a
   `PaperFormatAnchorable` olarak eklendi; `View > Panels` menüsüne "Paper Format" girişi
   (`OnShowPaperFormatPanel`) ve `OnResetLayout`'a dahil edildi — tam olarak istenen "toggler"
   (mevcut Tool Options/Active Pattern/Color Palette panel-toggle desenine uygun, ayrı bir modal
   dialog değil).
4. **`MainWindow.xaml.cs`**: `DocumentTitle` artık canlı olduğu için, sekme başlığının (
   `LayoutDocument.Title`, XAML Binding çalışmıyor — bkz. Bölüm 3/12) güncel kalması için
   `ApplyDocument` artık aktif ViewModel'in `PropertyChanged`'ına abone oluyor ve
   `DocumentTitle` değiştiğinde `MainDocument.Title`'ı elle güncelliyor.

**Doğrulama:** build 0 hata, Core testleri 22/22 yeşil (bu değişiklik Core'a dokunmadı, sadece
App katmanı), uygulama yeniden başlatıldı ve çalışıyor. **Görsel doğrulama kullanıcı tarafından
henüz yapılmadı** — bir sonraki turda Warp/Weft'i değiştirip Shift ile daire çizerek gerçekten
ekranda kare/daire göründüğünü teyit ettirmek gerekebilir.

## 23. "Daire çizimleri bozuk" şikayeti araştırıldı — algoritma zaten Bölüm 21'de düzeltilmişti; Paper Format panelini kaldırma girişimi kullanıcı tarafından geri alındı

Kullanıcı: "daire çizimleri hiç iyi değil en son sen pixel cord yapayım derken birşey yaptın ondan
sonra bozuldu... çizimler texcelle gibi olsun." Ayrı bir prob projesiyle (`/tmp/rugcad_probe2`,
kalıcı değil) `EllipseOutline`/`EllipseOutlineConnected`'ı 8'den 40'a kadar birçok boyutta ve
32×23 (asimetrik) kutuda ASCII olarak görselleştirdim — **her boyutta simetrik, düzgün, Texcelle
tarzı pixel-art daire/elips çıktısı doğrulandı, lumpy/bozuk bir çıktı GÖZLEMLENMEDİ.** Yani
Bölüm 21'in algoritma düzeltmesi (kaynak dosyası zaten build'den önceydi — exe 18:14, kaynak
18:05) doğru ve derlenmiş haliyle çalışıyor durumda.

**En olası açıklama:** kullanıcının "bozuk" dediği ekran görüntüsü, Bölüm 21 düzeltmesinden ÖNCE
zaten kanvasa işlenmiş (bake edilmiş) eski pikselerdi — bir düzeltme sadece YENİ çizilen
şekilleri etkiler, kanvasta zaten duran eski pikseller geriye dönük güncellenmez. **Kullanıcıya
bir sonraki turda temiz bir Ctrl+Z/New Design ile YENİ bir daire çizip sonucu tekrar
değerlendirmesi söylenmeli** — kod tarafında ek bir değişiklik yapılmadı çünkü mevcut algoritma
zaten doğrulandı.

Bu araştırma sürerken kullanıcı ayrıca ayrı bir talimat verdi: "paper format resize ekranında
olucak... mevcut eklediğin paper format ekranını kaldır sadece toggler ekle dedim... panels
içinde değil window altında olsun." Bunun üzerine Paper Format dockable panelini kaldırıp
`_Window` menüsü altında bir checkable toggle ile değiştirmeye başladım (ViewModel'e
`UsePhysicalPixelAspect`/`HasNonSquarePixelAspect` eklemeden önce, sadece XAML/code-behind
taşımasını yapmıştım) — ama kullanıcı hemen ardından **bu talimatı geri aldı**: "hatta mevcut
paper format kaldırma kalsın. ayrı bir şekilde ayarlarız." Bu yüzden Bölüm 22'deki
dockable "Paper Format" paneli (View > Panels > Paper Format) OLDUĞU GİBİ geri getirildi/korundu
— `_Window` menüsü altına taşıma YAPILMADI, `UsePhysicalPixelAspect` gibi yeni property'ler
EKLENMEDİ. **Bu konu ("paper format gerçek resize ekranına taşınsın, warp/weft=1x1 değilse
aktif olan bir toggle Window menüsünde olsun") kullanıcının "ayrı bir şekilde ayarlarız" dediği
gibi AÇIK/ERTELENMİŞ — ileride kesin talimat gelene kadar dockable panel haliyle bırakılmalı,
tekrar kaldırılmaya kalkışılmamalı.**

**Doğrulama:** geri alma sonrası build 0 hata, Core testleri 22/22 yeşil, uygulama yeniden
başlatıldı ve çalışıyor.

## 24. Ana toolbar'a "Paper Format" toggle butonu eklendi + View>Panels, Window menüsüne taşındı

Kullanıcı: "main bar(open,undo,redo olan kısım) bir tane paperformat togger ekle bu buton sana
dediğim. ve view Panels kısmını Window içerisine taşı ve Panels>reset layout da Window
içerisinde olsun." Yani Bölüm 23'te ertelenen iki şey şimdi netleşti:

1. **Ana toolbar'a (New/Undo/Redo olan ilk `ToolBar`) bir "Paper Format" `ToggleButton`
   eklendi.** Bu, Bölüm 22'deki dockable Paper Format panelinin YERİNE geçmiyor — panel hâlâ
   duruyor (Bölüm 23'te "kaldırma kalsın" denmişti) — bu buton, panelin Warp/Weft
   DEĞERLERİNİ değil, `DesignCanvas`'ın onları kullanıp kullanmadığını (yani ekranın fiziksel
   en/boy oranını mı yoksa düz kareyi mi göstereceğini) açıp kapatan ayrı bir şalter:
   - `DesignSurfaceViewModel.UsePhysicalPixelAspect` (yeni, varsayılan `true`) — `DesignCanvas.
     PixelHeight` artık bu `false` ise `PixelWidth`'e eşit dönüyor (eski düz-kare davranış),
     `true` ise Bölüm 22'deki warp/weft oranlı hesaplamayı kullanıyor.
   - `DesignSurfaceViewModel.HasNonSquarePixelAspect` (yeni, `WarpDensity != WeftDensity`) —
     butonun `IsEnabled`'ı buna bağlı: piksel zaten kareyse (1:1 warp/weft) toggle'ın hiçbir
     görsel etkisi olmaz, bu yüzden devre dışı bırakılıyor ("eğer 1x1 değil ise ... aktif
     olucak" talimatı böyle karşılanıyor).
   - `DesignCanvas.OnViewModelPropertyChanged` artık `UsePhysicalPixelAspect` değişimini de
     dinleyip kanvası yeniden boyutlandırıp yeniden çiziyor.
2. **`View > Panels` alt menüsü tamamen `Window` menüsüne taşındı** (Reset Layout dahil, çünkü
   o zaten Panels'in bir alt öğesiydi — ayrıca taşımaya gerek kalmadı, Panels'le birlikte geldi).
   `View` menüsünde artık sadece Zoom ve Theme kaldı; `Window` menüsü artık `Panels` alt menüsünü
   barındırıyor.

**Doğrulama:** build 0 hata, Core testleri 22/22 yeşil (App-katmanı değişikliği, Core'a
dokunulmadı), uygulama yeniden başlatıldı ve çalışıyor.

## 25. Küçük UI düzeltmeleri (Bölüm 24'ten sonra, önceki oturumda dokümantasyona işlenmemiş)

Üç küçük değişiklik yapıldı ama o sırada bu dosyaya işlenmedi — geriye dönük kayıt altına
alınıyor:

1. **"Reset Layout to Default" artık `Window > Panels` alt menüsünün İÇİNDE değil, `Window`
   menüsünün doğrudan bir üyesi** (Panels'in yanında, bir Separator ile ayrılmış kardeş öğe).
   Kullanıcı: "Reset To layout to default, Window içinde olsun Panels içinde değil."
2. **Pencil/Rectangle/Ellipse'in Size X/Y satırlarına, Paper Format panelindeki gibi düzenlenebilir
   bir `TextBox` eklendi** (önceden sadece salt-okunur bir `TextBlock` vardı, slider'ın yanında).
3. **Her araca (Pencil/Rectangle/Ellipse) bağımsız bir "Proportional" checkbox'ı eklendi.**
   Açıkken Size X veya Y'den biri değiştirildiğinde diğeri Paper Format'ın Warp/Weft oranına göre
   otomatik güncelleniyor — `DesignSurfaceViewModel.SetLinkedSize` fiziksel kare mantığını
   (`ApplyShapeModifiers`'daki AYNI oran) tool-size'a uyguluyor. Üç araç için ayrı, bağımsız kilit
   (`PencilSizeLinked`, `RectangleSizeLinked`, `EllipseSizeLinked`).
4. **Selection aracı için alt bilgi çubuğunun sağ altına `SelectionSizeText` eklendi** — yeni bir
   marquee sürüklenirken canlı, ya da bir seçim zaten varken genişlik x yükseklik ve piksel
   sayısını (`"W x H px (N)"`) gösteriyor; `DesignCanvas.UpdateSelectionSizeInfo`,
   `OnViewModelPropertyChanged`'da `Selection`/`CurrentTool` değişimini dinleyerek ve
   `OnMouseMove`'da canlı sürüklemeyi yakalayarak güncelleniyor.

**Doğrulama (o oturumda yapıldı ama burada eksik kalmıştı):** build 0 hata, Core testleri 22/22
yeşil her adımda, uygulama her adımda yeniden başlatılıp çalışır durumda bırakıldı.

## 26. Dosya Aç/Kaydet (.rugcad native format) + PNG/BMP içe-dışa aktarım — TAMAMLANDI

Bölüm 8'in yol haritasında 4. madde olan dosya I/O'su, kullanıcının "sıradaki adım nedir"
sorusuna karşılık sunulan seçeneklerden ("Dosya Aç/Kaydet" vs "Repeat önizleme + transform")
kullanıcının "Dosya Aç/Kaydet"i seçmesiyle uygulandı.

**Yeni: `RugCAD.Core.Serialization.RugCadDocumentSerializer`** (Core katmanı, framework-bağımsız):
- Kendi native `.rugcad` formatı — genel amaçlı bir serializer (JSON/XML) değil, elle yazılmış
  basit/versiyonlu bir binary layout: magic header (`"RUGCAD"`) + format versiyonu (şu an `1`) +
  isim + WarpDensity/WeftDensity + genişlik/yükseklik + palet (RGB üçlüleri) + piksel başına 1
  byte. Magic+versiyon sayesinde ileride format değişirse (ör. Bölüm 9'un Layer sistemi) eski
  dosyalar sessizce yanlış okunmak yerine açıkça reddedilebilir.
- `Save(Stream, DesignDocument, RugCadDocumentInfo)` / `Load(Stream) -> (DesignDocument,
  RugCadDocumentInfo)`. `RugCadDocumentInfo` = `(Name, WarpDensity, WeftDensity)`.
- `tests/RugCAD.Core.Tests/RugCadDocumentSerializerTests.cs`: round-trip testi (piksel+palet+info
  birebir geri geliyor) ve bozuk magic header'ı reddetme testi — Core testleri artık 22'den 24'e
  çıktı.

**Yeni: `RugCAD.App.Services.ImageImportExportService`** (App katmanı — SkiaSharp'a ihtiyaç
duyduğu için Core'da değil, canvas render'ı için zaten kullanılan SkiaSharp'ı tekrar kullanıyor,
ikinci bir görüntü kütüphanesi eklemek yerine):
- `Import(filePath)`: bir PNG/BMP dosyasını okuyup, görüntüdeki HER FARKLI rengi sırasıyla bir
  palet girdisine çeviren ve pikselleri o indekslerle dolduran bir `DesignDocument` üretir.
  256'dan fazla farklı renk varsa (`DesignDocument` piksel başına 1 byte/indeks kullanıyor, bkz.
  Core) açık bir `InvalidDataException` fırlatır — foto gibi görüntüler için posterize/quantize
  gerektiği söylenir, bu adım UYGULANMADI (kapsam dışı, halı deseni gibi düz-renk pixel-art için
  zaten gereksiz).
- `Export(DesignDocument, filePath, ImageFormat)`: paleti gerçek renklere düzleştirip PNG veya BMP
  olarak yazar.

**`DesignSurfaceViewModel`**: yeni `FilePath` property'si (nullable, mutable) — bir tasarımın
diskteki son konumunu tutuyor, Save'in doğrudan mı yazacağına yoksa Save As'a mı düşeceğine karar
vermek için kullanılıyor. `FileType` zaten constructor'dan ayarlanabiliyordu (RUGCAD/PNG/BMP).

**`MainWindow`**: 
- `_File` menüsüne gerçek `Open...`/`Save`/`Save As...`/`Import Image...`/`Export Image...`
  girişleri eklendi (önceden bilinçli olarak sadece `New` vardı — Bölüm 15.3'te "sahte buton
  eklemeyelim, dosya I/O'su gerçek olunca eklenir" denmişti; şimdi gerçek olduğu için eklendi).
  Ctrl+N/O/S için `ApplicationCommands.New/Open/Save` + `CommandBinding` kullanıldı (Click
  handler'ları `ExecutedRoutedEventArgs`'ın `RoutedEventArgs`'tan türediği gerçeğinden
  yararlanan ince sarmalayıcılarla yeniden kullanıldı).
- Ana toolbar'a da gerçek `Open`/`Save` butonları eklendi (New'in yanına); eski "sahte buton
  eklemeyelim" yorumu artık geçersiz olduğu için kaldırıldı.
- `Open`: `.rugcad` dosyasını `RugCadDocumentSerializer.Load` ile okuyup yeni bir
  `DesignSurfaceViewModel` kurup `ApplyDocument` ile değiştiriyor (New Design ile aynı desen).
- `Save`/`Save As`: `FilePath` doluysa ve `FileType == "RUGCAD"` ise doğrudan üzerine yazıyor,
  değilse `SaveFileDialog` açıyor.
- `Import Image`/`Export Image`: `ImageImportExportService`'i çağırıp sonucu (import için) yeni
  bir ViewModel olarak `ApplyDocument`'a veriyor / (export için) mevcut `Document`'ı dosyaya
  yazıyor. Hatalar `MessageBox` ile kullanıcıya gösteriliyor (try/catch, uygulamayı çökertmiyor).

**Doğrulama:** Core testleri 24/24 yeşil (2 yeni serializer testi dahil), App build 0 hata,
uygulama yeniden başlatıldı ve çalışıyor. **UI üzerinden gerçek bir Aç/Kaydet/İçe-Dışa Aktar
denemesi (dosya diyalogları tıklanarak) bu oturumda YAPILMADI** — WPF dosya diyalogları otomatik
test ortamından tıklanamıyor; alttaki serializer mantığı unit test ile doğrulandı ama uçtan uca
UI akışı henüz kullanıcı tarafından denenmedi. **Bir sonraki adımda kullanıcının Open/Save/Import/
Export'u gerçek dosyalarla deneyip sonucu bildirmesi gerekiyor.**

## 27. Open/Save As diyalogları Texcelle'deki gibi TEK diyalog + çoklu tür dropdown'ına birleştirildi; desteklenen görüntü formatları genişletildi

Kullanıcı, Texcelle'nin Open/Save As diyaloglarının ekran görüntüsünü paylaşıp ("Dosya türü"
dropdown'ında .pat/.des/.bmp/.tif/.eps/.gif/.jpg/.pcx/.pcd/.pct/.png/.psd/.ras/.tga/.iff/.dxf/
.dwg/.crd gibi onlarca format listeleniyor) "open ve save as da texcelle deki gibi türlere destek
versin" dedi.

**Bilinçli sınır:** Texcelle'nin listesindeki çoğu format (TIFF, PSD, EPS, PCX, Kodak PhotoCD,
Macintosh PICT, SUN Raster, TGA, IFF, AutoCAD DXF/DWG, kendi .pat/.des/.crd formatları, Sophis
Design) RugCAD tarafından GERÇEKTEN okunamıyor/yazılamıyor — bunları listeye eklemek, projede
daha önce Bölüm 15.3'te reddedilen türden bir "sahte buton/format" olurdu (kullanıcı hiçbir zaman
gerçekten açamayacağı bir seçenek görür). Bunun yerine SkiaSharp'ın gerçekten decode/encode
edebildiği format seti genişletildi ve TEK bir Open diyaloğu + TEK bir Save As diyaloğu ile
sunuldu (Texcelle'nin "bir diyalog, tür dropdown'ında birçok format" UX'ini taklit ediyor, ama
sadece gerçekten çalışan formatlarla):

- **`ImageImportExportService`**: `ImageFormat` enum'ı `Png, Bmp, Jpeg, Gif, Webp` olarak
  genişletildi (öncesi: sadece `Png, Bmp`). `Export` artık hepsini `SKEncodedImageFormat`'a
  eşliyor (JPEG için kalite 92, diğerleri kayıpsız/100). Yeni `FormatFromExtension(string)`
  yardımcı metodu bir dosya uzantısını `ImageFormat?`'e çeviriyor (tanınmayan uzantı için `null`).
- **`MainWindow`**: `OnOpenDesign` ve `OnImportImage`/`OnExportImage` TEK bir Open akışında
  birleştirildi (ayrı "Import Image..."/"Export Image..." menü girdileri kaldırıldı) —
  `OpenFileDialog.Filter` artık `.rugcad;*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.webp` (+ "All Files")
  arasından seçim sunuyor, uzantıya göre native yükleme mi yoksa
  `ImageImportExportService.Import` mi çağrılacağına karar veriliyor. `SaveDesignAs`/`SaveToPath`
  aynı şekilde birleştirildi — `SaveFileDialog`'un tür dropdown'ı `.rugcad` VEYA bir görüntü
  formatı sunuyor, uzantıya göre native `RugCadDocumentSerializer.Save` mı yoksa
  `ImageImportExportService.Export` mi çağrılacağı `FormatFromExtension` ile belirleniyor.
  `OnSaveDesign` (Ctrl+S) artık `FileType == "RUGCAD"` kontrolü YAPMIYOR — sadece `FilePath` dolu
  mu diye bakıp, doluysa o dosyanın uzantısına göre doğru formatta üzerine yazıyor (yani PNG'den
  açılmış bir tasarımda Ctrl+S o PNG'nin üzerine tekrar PNG olarak yazar, RugCAD'e zorlamaz).

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil (bu değişiklik Core'a dokunmadı, sadece
App katmanı — `ImageFormat` enum'ı App'te), uygulama yeniden başlatıldı ve çalışıyor. **UI
üzerinden gerçek dosya diyaloglarıyla deneme yine YAPILMADI** (bkz. Bölüm 26'nın aynı notu) —
kullanıcının Open/Save As'ı tür dropdown'ından farklı formatlar seçerek denemesi gerekiyor.

## 28. Save As → BMP çöküyordu: `SKImage.Encode(Bmp/Gif, ...)` bu SkiaSharp sürümünde SESSİZCE null dönüyor — BMP için elle yazan encoder eklendi, GIF export açıkça desteklenmiyor olarak işaretlendi

Kullanıcı Save As ile `.bmp` seçip kaydetmeye çalıştığında "Could not save '...\asdsa.bmp':
Object reference not set to an instance of an object." hatası aldı (ekran görüntüsü paylaşıldı).

**Kök neden:** ayrı bir prob projesiyle (`/tmp/rugcad_probe3`, kalıcı değil) doğrudan test
edildi — bu projede kullanılan SkiaSharp sürümünde `SKImage.Encode(SKEncodedImageFormat.Png/
Jpeg/Webp, ...)` düzgün çalışıyor ama `SKEncodedImageFormat.Bmp` VE `.Gif` için `Encode(...)`
**sessizce `null` dönüyor** (exception fırlatmıyor). `ImageImportExportService.Export`'taki eski
kod bu `null`'ı hiç kontrol etmeden doğrudan `data.SaveTo(fileStream)` çağırıyordu — `data` null
olduğu için bu satır `NullReferenceException` fırlatıyordu, kullanıcıya "Object reference not set
to an instance of an object" gibi anlamsız bir mesaj olarak yansıyordu.

**Düzeltme:**
- **BMP için**: SkiaSharp'ın encode API'sine güvenmek yerine, `ImageImportExportService`'e elle
  yazan bir `WriteBmp` metodu eklendi — standart, sıkıştırmasız 24-bit BMP (BITMAPFILEHEADER +
  BITMAPINFOHEADER, satırlar alttan yukarı, her satır 4 byte'a yuvarlanmış padding ile) formatın
  kendi kurallarına göre elle yazılıyor. Round-trip (`Export` sonra `Import`) ayrı bir prob ile
  doğrulandı: piksel renkleri birebir geri geliyor.
- **GIF için**: gerçek bir GIF encoder'ı (LZW sıkıştırma gerektirir) yazmak kapsam dışı bırakıldı
  — bunun yerine `Export`, GIF seçildiğinde artık NRE ile çökmek yerine açık, eyleme geçirilebilir
  bir `NotSupportedException` fırlatıyor ("GIF export isn't supported ... Save as PNG, BMP, JPEG
  or WebP instead"). **`MainWindow`'daki `SaveAsFilter`'dan GIF tamamen ÇIKARILDI** (Save As tür
  dropdown'ında artık görünmüyor) — GIF sadece `OpenFilter`'da (İÇE AKTARIM/decode) kalmaya devam
  ediyor, çünkü decode gerçekten çalışıyor, sadece encode çalışmıyor. Bu, kullanıcıya çalışmayan
  bir seçeneği göstermemek için bilinçli bir karar (Bölüm 15.3/26/27'nin "sahte buton/format
  gösterme" ilkesiyle tutarlı).
- PNG/JPEG/WebP encode yolu da artık `Encode(...)`'un döndürdüğü `null`'ı kontrol edip
  (teorik olarak, ileride farklı bir SkiaSharp sürümünde/formatta tekrar olursa) NRE yerine yine
  açık bir `NotSupportedException` fırlatacak şekilde sağlamlaştırıldı.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, App'in gerçek `Export`/`Import`
fonksiyonları ayrı bir prob projesinden (App'e `ProjectReference` ile) doğrudan çağrılarak BMP
round-trip'i somut olarak doğrulandı (`Exported ... size=118 bytes`, reimport edilen pikseller
orijinal renklerle birebir eşleşti). Uygulama yeniden başlatıldı ve çalışıyor. **GIF export'un
artık dropdown'da hiç görünmediği ve BMP'nin gerçekten kaydettiği kullanıcı tarafından UI
üzerinden de teyit edilmeli** (otomatik dosya diyaloğu testi hâlâ mümkün değil).

## 29. "bmp paper format değerlerini girerek kaydetmiyor" — görüntü formatlarının Warp/Weft/Name'i saklayacak yeri yok, sidecar dosyayla çözüldü

Kullanıcı BMP kaydının Paper Format'ta (Warp/Weft) girdiği değerleri korumadığını fark etti.
**Bu bir bug değil, formatın kendi doğal sınırı**: BMP/PNG/JPEG/GIF/WebP true-color görüntü
formatlarıdır, RugCAD'e özel Warp/Weft yoğunluğu veya Name için hiçbir alanları yok — bunlar
sadece `.rugcad` native formatında (Bölüm 26) tutulabiliyordu. Eskiden bir görüntüye export
edilip sonra tekrar açılan bir tasarım, her zaman varsayılan 32/23'e dönüyordu.

**Çözüm — sidecar meta dosyası:** `ImageImportExportService`'e `WriteMetadataSidecar`/
`ReadMetadataSidecar` eklendi. Export edilen her görüntünün yanına, aynı isimde + `.rugcadmeta`
uzantılı küçük bir metin dosyası yazılıyor (ör. `asdsa.bmp` yanında `asdsa.bmp.rugcadmeta`,
3 satır: Name / WarpDensity / WeftDensity). Bu dosya BMP/PNG dosyasının kendisini hiç
değiştirmiyor — başka bir programda görüntüyü açan biri onu normal bir BMP/PNG olarak görür,
yanındaki `.rugcadmeta` dosyasını görmezden gelir (ya da hiç görmez). Sadece RugCAD'in kendi
Open'ı bu sidecar'ı arayıp buluyorsa Name/Warp/Weft'i geri yüklüyor; yoksa (elde başka bir
programdan gelen düz bir görüntü) eskisi gibi dosya adını Name, 32/23'ü Warp/Weft olarak
kullanmaya devam ediyor.

**Değişiklikler:**
- `MainWindow.SaveToPath`: görüntü formatına export ettikten hemen sonra
  `WriteMetadataSidecar(path, new RugCadDocumentInfo(viewModel.Name, viewModel.WarpDensity,
  viewModel.WeftDensity))` çağrılıyor.
- `MainWindow.OnOpenDesign`: görüntü formatı içn `ReadMetadataSidecar(dialog.FileName)`
  çağrılıyor; sidecar varsa `Name`/`WarpDensity`/`WeftDensity` ondan alınıyor, yoksa eski
  davranış (dosya adı + varsayılan 32/23) korunuyor.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, ayrı bir prob projesiyle
`WriteMetadataSidecar`→`ReadMetadataSidecar` round-trip'i somut olarak doğrulandı
(`"MyDesign" 50/40` birebir geri geldi). Uygulama yeniden başlatıldı ve çalışıyor. **Kullanıcının
UI üzerinden gerçek bir "Paper Format'ı değiştir → BMP'ye kaydet → kapat → aynı BMP'yi Aç →
Paper Format'ın korunduğunu gör" akışını denemesi gerekiyor** — otomatik dosya diyaloğu testi
hâlâ mümkün değil.

## 30. Sidecar dosyası kaldırıldı (metadata artık görüntünün İÇİNE gömülüyor), kalite bulunamazsa 10/10 varsayılıyor, sürükle-bırak ile dosya açma + ÇOKLU DESEN (tab) desteği eklendi

Bu turda kullanıcı art arda dört talimat verdi:

1. "rugcadmeta diye birşey oluşturuyorsun bunu yapma mesela bak texcelle içerisini açabilir isen
   orda yapılan şeyi alabilirsin." — Bölüm 29'daki `.rugcadmeta` sidecar dosyası istenmiyor.
2. "birde açılan her desenin kalitesini okuttur. eğer kalite yok ise 10/10 olarak açtır." —
   açılan HER tasarımın kalitesi (Warp/Weft) okunmalı; yoksa varsayılan 32/23 değil 10/10.
3. "ve şuan program içerisine sürükle bırakarakta dosyayı açabilsin." — sürükle-bırak ile dosya
   açma.
4. "ve çoklu desende açılsın." — birden fazla tasarım aynı anda, ayrı sekmelerde açık olabilmeli.

**1) Sidecar kaldırıldı, metadata artık dosyanın İÇİNE gömülü:** Texcelle'nin kurulu dosyaları
arasında (yardım dosyası, ini'ler, tek bir splash BMP) gerçek bir export edilmiş .bmp/.des örnek
dosyası ya da kaynak kod bulunamadı, yani onların TAM OLARAK nasıl yaptığı reverse-engineer
edilemedi. Onun yerine, format-bağımsız, GERÇEKTEN çalışan bir teknik seçildi ve İSPATLANDI: her
export formatının kendi verisinden SONRA (dosyanın sonuna) küçük bir "trailer" ekleniyor. Ayrı
prob projeleriyle şu doğrulandı — SkiaSharp'ın PNG/JPEG/WebP decode'u ve elle yazılan BMP okuyucu,
kendi bildirdikleri görüntü yapısını tükettikten sonra durup dosyanın geri kalanını (trailer'ı)
tamamen görmezden geliyor; hiçbiri bozulmadı. Trailer formatı: `[magic:8]["RUGCADX1"]
[nameLen:4][nameBytes][warp:4][weft:4][toplamTrailerUzunluğu:4]` — son 4 byte trailer'ın nerede
başladığını geriye doğru saymadan bulmayı sağlıyor; magic kontrolü, RugCAD dışından gelen düz bir
görüntünün son birkaç byte'ının yanlışlıkla trailer sanılmasını engelliyor.
- `ImageImportExportService.WriteMetadataSidecar`/`ReadMetadataSidecar` SİLİNDİ, yerine
  `WriteEmbeddedMetadata(imagePath, info)` (export edilen dosyaya `FileMode.Append` ile trailer
  ekliyor) ve `ReadEmbeddedMetadata(imagePath)` (dosyanın son byte'larından trailer'ı okuyup
  magic'i doğruluyor, yoksa `null` dönüyor) eklendi. **Artık hiçbir `.rugcadmeta` dosyası
  oluşturulmuyor — tek dosya, Texcelle'nin muhtemelen yaptığı gibi.**

**2) Kalite (Warp/Weft) bulunamazsa 10/10:** `MainWindow`'da yeni `DefaultQualityWhenUnknown =
10` sabiti — bir görüntü açılırken `ReadEmbeddedMetadata` `null` dönerse (RugCAD dışından gelen
düz bir görüntü), `WarpDensity`/`WeftDensity` artık eski varsayılan (constructor'daki 32/23)
yerine 10/10 olarak ayarlanıyor. `.rugcad` dosyaları zaten her zaman kendi Warp/Weft'ini
taşıdığından bu sadece görüntü-açma yolunu etkiliyor.

**3) Sürükle-bırak:** `MainWindow.xaml`'a `AllowDrop="True"` + `DragEnter="OnWindowDragEnter"` +
`Drop="OnWindowDrop"` eklendi. `OnWindowDrop`, bırakılan her dosya yolu için ortak
`OpenFileAsNewTab` metodunu çağırıyor (Open diyaloğuyla birebir aynı mantık — uzantıya göre
`.rugcad` mi yoksa görüntü mü olduğuna karar veriyor).

**4) Çoklu desen (tab) desteği — mimari değişiklik:** Önceden tek bir sabit
`<avalonDock:LayoutDocument x:Name="MainDocument">` vardı, `ApplyDocument` onun `DataContext`'ini
DEĞİŞTİRİYORDU (yani aslında hep TEK bir sekme). Şimdi:
- `MainWindow.xaml`'da `MainDocument` kaldırıldı, yerine boş bir
  `<avalonDock:LayoutDocumentPane x:Name="DocumentPane" />` var — sekmeler artık kod tarafından
  runtime'da ekleniyor.
- Yeni `OpenDocumentTab(DesignSurfaceViewModel)`: kendi `DesignCanvas`'ı ve `DataContext`'iyle
  yeni bir `LayoutDocument` oluşturup `DocumentPane.Children`'a ekliyor, aktif yapıyor. Başlık
  (yine `LayoutDocument.Title`'ın XAML Binding'i çalışmadığı için) `viewModel.PropertyChanged`
  aboneliğiyle canlı tutuluyor.
- `DockingManager`'a `ActiveContentChanged="OnActiveContentChanged"` eklendi — kullanıcı sekme
  değiştirdiğinde (veya yeni sekme aktif olduğunda), `Window.DataContext` (dolayısıyla Tool
  Options/Paper Format/Color Palette panelleri VE toolbar/menü'deki Undo/Redo/Zoom/vs. komutları)
  otomatik olarak o an aktif olan sekmenin ViewModel'ine geçiyor. Yan paneller hâlâ tek bir
  `{Binding}` ile Window DataContext'ine bağlı — mimari olarak "her zaman aktif tasarımı göster"
  deseni korunuyor, sadece artık birden fazla tasarım eşzamanlı canlı kalabiliyor.
- `OnNewDesign` ve `OnOpenDesign`/`OnWindowDrop` artık HER ZAMAN yeni bir sekme AÇIYOR (eskiden
  `ApplyDocument` mevcut tek sekmenin içeriğini DEĞİŞTİRİYORDU). `OnOpenDesign`'da
  `OpenFileDialog.Multiselect = true` da eklendi — birden fazla dosya seçilirse hepsi ayrı
  sekmeler olarak açılıyor.
- `Save`/`Save As` hâlâ `_activeViewModel` üzerinden çalışıyor, artık `OnActiveContentChanged`
  tarafından güncel tutuluyor.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil (bu değişiklikler App katmanında, Core'a
dokunulmadı). Uygulama yeniden başlatıldı ve çalışıyor. **UI üzerinden gerçek doğrulama
YAPILMADI** — kullanıcının denemesi gerekenler: (a) birden fazla dosyayı Open ile veya sürükle-
bırakla açıp gerçekten ayrı sekmeler halinde göründüğünü, (b) sekmeler arasında geçince Tool
Options/Paper Format panellerinin doğru tasarımı gösterdiğini, (c) kalitesi olmayan bir görüntüyü
açınca gerçekten 10/10 geldiğini, (d) bir .rugcadmeta dosyası ARTIK oluşturulmadığını ve
export→import sonrası Paper Format'ın yine de korunduğunu teyit etmesi.

## 31. GERÇEK KEŞİF: Texcelle Paper Format'ı BMP'nin STANDART XPelsPerMeter/YPelsPerMeter alanlarında saklıyor — RugCAD artık aynı alanları kullanıyor; ayrıca zoom yaparken donma sorunu bulunup düzeltildi

Kullanıcı, C:\Users\DesenS\Desktop\MH1_B228A_P1611_0051.bmp adında GERÇEK bir Texcelle tasarım
dosyası paylaştı ve iki ekran görüntüsüyle gösterdi: Texcelle bu dosyayı "48 x 75" Paper Format
ile açıyor, RugCAD ise (Bölüm 30'daki mantıkla, dosyada bizim trailer'ımız olmadığı için) 10/10
varsayılanıyla açıyordu. Bu, Bölüm 29/30'daki "Texcelle'in gerçekte ne yaptığını bilmiyoruz"
belirsizliğini SOMUT VERİYLE çözme fırsatıydı.

**Keşif:** PowerShell ile dosyanın BITMAPFILEHEADER+BITMAPINFOHEADER'ını byte byte okudum:
`XPelsPerMeter = 1890`, `YPelsPerMeter = 2953` (standart BMP header'ın 38. ve 42. byte
offset'lerindeki, spesifikasyonda "yazıcı çözünürlüğü" için ayrılmış alanlar). Metre başına
piksel değerini inç başına çevirince (`değer * 0.0254`): `1890 * 0.0254 = 48.006 ≈ 48`,
`2953 * 0.0254 = 75.03 ≈ 75` — **Texcelle'in ekranda gösterdiği "48 x 75" ile BİREBİR eşleşiyor.**
Yani **Texcelle, Warp/Weft yoğunluğunu hiçbir özel/proprietary alanda değil, BMP'nin standart,
belgeli çözünürlük alanlarında (çoğu yazılımın DPI için kullandığı alanlar) saklıyor** — Bölüm
30'da "onların tam tekniğini reverse-engineer edemedik" denilen şey artık somut olarak biliniyor.

**Yapılan değişiklikler:**
- `ImageImportExportService.WriteBmp`: artık `warpDensity`/`weftDensity` parametreleri alıyor ve
  bunları `xPelsPerMeter = round(warp / 0.0254)` / `yPelsPerMeter = round(weft / 0.0254)` olarak
  BMP header'ının GERÇEK, standart alanlarına yazıyor (eskiden hardcoded 2835/2835 "~72 DPI"
  placeholder'ı vardı). **Sonuç: RugCAD'in kendi BMP export'ları artık Texcelle'in de
  okuyabileceği şekilde density taşıyor — tek yönlü değil, iki yönlü uyumluluk.**
- Yeni `ImageImportExportService.ReadBmpDensity(filePath)`: bir BMP'nin header'ındaki
  XPelsPerMeter/YPelsPerMeter'i okuyup ters çeviriyor (`round(pikselPerMetre * 0.0254)`), ikisi de
  0'dan büyükse `(Warp, Weft)` dönüyor, değilse `null`. Ayrı bir prob projesinden GERÇEK dosyaya
  karşı çalıştırılıp doğrulandı: `Warp=48 Weft=75` — birebir Texcelle ile eşleşti.
- `Export`'un imzasına `int warpDensity = 0, int weftDensity = 0` eklendi (sadece BMP için
  kullanılıyor, diğer formatlar için anlamsız/yok sayılıyor).
- `MainWindow.OpenFileAsNewTab`: kalite/isim önceliği artık üç seviyeli — (1) kendi
  `ReadEmbeddedMetadata` trailer'ımız (RugCAD'in kendi export'u ise, her format için TAM isim +
  Warp + Weft), (2) trailer yoksa VE dosya BMP ise `ReadBmpDensity` (Texcelle dahil BAŞKA
  yazılımlardan gelen düz BMP'ler için), (3) hiçbiri yoksa Bölüm 30'daki 10/10 varsayılanı.
- `MainWindow.SaveToPath`: BMP'ye export ederken artık `viewModel.WarpDensity`/`WeftDensity`
  `Export`'a geçiriliyor (BMP header'ına yazılsın diye); trailer da hâlâ ayrıca yazılıyor (Name
  bilgisini taşımak için, BMP'nin standart alanlarında isim saklayacak bir yer yok).

**Ayrıca — zoom yaparken donma sorunu bulunup düzeltildi:** kullanıcı "bir desende zoom in out
yapınca program hantallaştı ve dondu" dedi (bu gerçek 240x600'lük Texcelle örneğiyle test
ederken fark edilmiş olmalı — 144.000 hücre). **Kök neden:** `DesignCanvas.OnPaintSurface` HER
YENİDEN ÇİZİMDE (her zoom adımında dahil) dokümanın HER TEK hücresini tek tek geziyor, hücre
başına 2 `DrawRect` çağrısı (dolgu + ızgara çizgisi) yapıyordu — 240x600 bir tasarımda bu
288.000 çizim komutu, HER SEFERINDE, UI thread'inde senkron. Fare tekerleği art arda birkaç kez
çevrildiğinde bu ağır yeniden çizimler bitmeden yenileri kuyruğa giriyor, uygulama tamamen
kilitleniyordu.
- **Düzeltme:** yeni `_bakedBitmap` (bir `SKBitmap`, dokümanla birebir aynı boyutta, 1
  doküman-pikseli = 1 bitmap-pikseli) sadece doküman GERÇEKTEN değiştiğinde (`OnDocumentChanged`)
  veya yeni bir doküman yüklendiğinde (`OnDataContextChanged`) yeniden oluşturuluyor — zoom'da
  DEĞİL. `OnPaintSurface` artık her karede sadece bu bitmap'i `PixelWidth`/`PixelHeight`'e göre
  ÖLÇEKLENMİŞ TEK BİR `DrawBitmap` çağrısıyla çiziyor (`SKSamplingOptions(SKFilterMode.Nearest,
  SKMipmapMode.None)` ile — pixel-art netliği korunuyor, bulanıklaşmıyor). Izgara çizgileri de
  artık hücre başına değil, `O(genişlik+yükseklik)` çizgi olarak çiziliyor (240+600=840 çizgi,
  eskiden 144.000 dikdörtgen) VE sadece `PixelWidth/Height >= 4px` iken çiziliyor (uzaktan
  zaten görünmeyen ızgarayı hiç çizmemek için). **Sonuç: zoom artık dokümanın piksel sayısından
  bağımsız, sabit maliyetli bir işlem — donma sorunu kökten çözülmüş olmalı.**

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, `ReadBmpDensity` gerçek Texcelle dosyasına
karşı ayrı bir prob ile doğrulandı (`Warp=48 Weft=75`). Uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının denemesi gerekenler:** (a) bu gerçek Texcelle BMP'sini RugCAD'de açıp Paper
Format'ın gerçekten 48/75 geldiğini görmek, (b) o dosyayı tekrar BMP olarak kaydedip yeniden
açarak (veya Texcelle'de açarak) density'nin korunduğunu doğrulamak, (c) büyük bir tasarımda
(bu 240x600 örnek gibi) zoom in/out yaparken artık donma olmadığını teyit etmek.

## 32. Zoom hâlâ kasıyordu — Bölüm 31'in bitmap-cache düzeltmesi yetmedi; GERÇEK kök neden `SKElement`'in kontrolün kendisini zoom'la birlikte DEV BOYUTA büyütmesiydi — kullanıcının eski, hızlı çalışan bir WinForms kontrolüyle kıyaslaması sayesinde bulundu

Kullanıcı Bölüm 31'in düzeltmesinden sonra da "yok halen kasıyor" dedi ve kendi eski bir
projesinden (`CerpAPP`) `PixelBitmapControl.cs` adlı, Texcelle gibi akıcı çalıştığını söylediği
bir WinForms kontrolünü örnek gösterdi. O dosyayı diskte bulup (`C:\Users\DesenS\source\repos\
yldrmhasan\CerpAPP\CerpAPP\UI\Controls\PixelBitmapControl.cs`) satır satır incelemek, Bölüm 31'in
GÖRMEDİĞİ asıl kök nedeni ortaya çıkardı.

**Gerçek kök neden:** `DesignCanvas.xaml.cs`'teki `UpdateCanvasSize()`, `SkElement.Width`/
`Height`'i her zoom adımında `doc.Width * PixelWidth` / `doc.Height * PixelHeight` olarak
ayarlıyordu — yani zoom arttıkça **kontrolün kendisi** (ve onun arkasındaki SkiaSharp render
yüzeyi) fiziksel olarak devasa büyüyordu. 240x600'lük örnek tasarımda zoom 8x-16x gibi
seviyelerde bu, milyonlarca pikselllik bir yazılımsal (`SKElement` — GPU değil, CPU/yazılım
render yüzeyi) render hedefinin HER ZOOM ADIMINDA yeniden ayrılıp TAMAMEN yeniden çizilmesi
demekti. Bölüm 31'in `_bakedBitmap` önbelleklemesi hücre-başı çizim maliyetini gidermişti ama
BU sorunu (devasa render yüzeyi tahsisi/taşınması) hiç dokunmamıştı — o yüzden "hâlâ kasıyor"du.

**Referans kontrolün yaptığı (ve şimdi RugCAD'in de yaptığı) şey:** `PixelBitmapControl`,
kontrolün kendi boyutunu ASLA zoom'a göre değiştirmiyor — sabit bir viewport (ScrollableControl)
içinde `Graphics.DrawImage(_image, destination)` ile TEK BİR ölçekli çizim yapıyor, kaydırma/
pan'i kendi `_offset` alanıyla, GERÇEK bir scroll container'a değil, sadece çizim sırasında
eklenen bir öteleme olarak uyguluyor.

**Yapılan değişiklikler:**
- `DesignCanvas.xaml`: `ScrollViewer` tamamen kaldırıldı. `SkElement` artık Grid hücresini
  dolduruyor (stretch), boyutu HİÇBİR ZAMAN zoom'a göre değişmiyor — render yüzeyi her zaman
  sadece görünür viewport kadar (birkaç yüz-bin piksel, milyonlarca değil).
- Yeni `_panOffsetX`/`_panOffsetY` (double) + `ScreenX(designX)`/`ScreenY(designY)` yardımcıları
  — tasarım koordinatını ekran koordinatına çevirirken artık `x * PixelWidth` yerine
  `_panOffsetX + x * PixelWidth` kullanılıyor. `OnPaintSurface`, seçim overlay'i, şekil
  önizlemesi, marquee, floating capture — hepsi bu iki yardımcı üzerinden geçirildi.
  `ToDesignCoordinates` de aynı şekilde ters çevriliyor.
- `UpdateCanvasSize()` TAMAMEN KALDIRILDI (artık hiçbir yerde kontrol boyutu ayarlanmıyor).
- **Zoom artık mouse imlecine göre "anchor" ediliyor** (`OnPreviewMouseWheel`): Ctrl+tekerlek
  zoom yaparken imlecin altındaki tasarım koordinatı zoom sonrasında yine imlecin altında kalacak
  şekilde `_panOffsetX/Y` otomatik ayarlanıyor — referans kontrolün `ZoomAt` metoduyla birebir
  aynı fikir. Düz tekerlek artık dikey, Shift+tekerlek yatay pan yapıyor (eskiden bunu
  ScrollViewer bedava sağlıyordu, artık elle yapılıyor).
- Orta-tık pan (`OnPanMouseDown/Up`) artık `Scroller.ScrollTo...` yerine doğrudan
  `_panOffsetX/Y`'yi güncelliyor.
- Skia zaten çizimi yüzey sınırlarına kendiliğinden clip'lediği için, pan ile görünmeyen kısımlar
  otomatik "bedavaya" atlanıyor — ekstra bir viewport-culling kodu yazmaya gerek kalmadı.

**Sonuç:** artık zoom seviyesinden VE tasarım boyutundan bağımsız olarak, her karede render
edilen yüzey her zaman sabit (viewport) boyutta — daha önce zoom'un kendisi devasa bir yeniden
tahsis+yeniden çizim tetikliyordu, şimdi sadece küçük bir ölçekli `DrawBitmap` çağrısı.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil (App-katmanı, Core'a dokunulmadı),
uygulama yeniden başlatıldı ve çalışıyor. **Kullanıcının gerçek 240x600 Texcelle örneğinde
zoom in/out yaparak artık gerçekten akıcı olduğunu teyit etmesi gerekiyor** — bu ikinci
denemenin de yetersiz kalması ihtimaline karşı, bir sonraki adımda `SKElement` yerine GPU
hızlandırmalı `SKGLElement`'e geçiş de değerlendirilebilir (şu an yazılımsal render kullanılıyor).

## 33. Window menüsüne açık sekmelerin listesi + View menüsüne Scrollbars aç/kapa toggle'ı eklendi

Kullanıcı: "menu barlarda window menüsünde butonları olsun ve açılıp kapanabilsin. scroolviewer
view menüsünde açılır kapana bilsin."

**1) `_Window` menüsüne açık desen sekmelerinin CANLI listesi eklendi.** Her açık
`LayoutDocument` (bkz. Bölüm 30'un çoklu-sekme mimarisi) artık `_Window` menüsünde işaretlenebilir
bir satır olarak görünüyor — işaretli olan hangi sekmenin aktif olduğunu gösteriyor, tıklamak o
sekmeye geçiyor. Liste STATİK DEĞİL: `MainWindow.xaml`'da `_Window` `MenuItem`'ına
`SubmenuOpened="OnWindowMenuOpened"` eklendi, her açılışta önceki turun eklediği öğeler
kaldırılıp `DocumentPane.Children`'daki güncel sekme listesinden SIFIRDAN yeniden kuruluyor (bir
`WindowMenuTabsEnd` separator'ünün hemen üstüne ekleniyor) — sekmeler her an açılıp/kapanıp/
değişebildiği için statik bir liste anında bayatlardı.
- `MainWindow.xaml.cs`: `_windowMenuTabItems` listesi önceki turun `MenuItem`'larını takip
  ediyor; `OnWindowMenuOpened` bunları kaldırıp `DocumentPane.Children.OfType<LayoutDocument>()`
  üzerinden yeniden oluşturuyor, her birinin `Click`'i o `LayoutDocument.IsActive = true`
  yapıyor (bu zaten `OnActiveContentChanged` üzerinden tüm panelleri/toolbar'ı o sekmeye
  geçiriyor — Bölüm 30'da kurulan mekanizma).

**2) `_View` menüsüne "Scrollbars" işaretlenebilir toggle'ı eklendi** — Bölüm 32'de `ScrollViewer`
kaldırılıp kendi pan/scrollbar mekanizmamıza geçilmişti; bu, o scrollbar çiftinin (yatay+dikey,
her `DesignCanvas`'ın kendi içinde) görünürlüğünü açüp kapatan genel (doküman-bazlı değil,
UYGULAMA GENELİ) bir ayar:
- Yeni `RugCAD.App.Services.CanvasViewOptions` (statik sınıf, `ShowScrollbars` bool +
  `ShowScrollbarsChanged` event) — tüm açık `DesignCanvas` sekmelerini AYNI ANDA etkilemesi
  gerektiği için (belirli bir dokümanın ViewModel'i değil, pencere/uygulama seviyesinde bir
  görünüm ayarı) tek bir paylaşılan statik durak olarak tutuluyor.
- Her `DesignCanvas`, constructor'da `CanvasViewOptions.ShowScrollbarsChanged`'a abone oluyor;
  değiştiğinde kendi `HorizontalScrollBar`/`VerticalScrollBar`'ının `Visibility`'sini güncelliyor.
- `MainWindow`'daki `Scrollbars` `MenuItem`'ı `IsCheckable`, `Click`'te
  `CanvasViewOptions.ShowScrollbars`'ı flip'liyor; başlangıç durumu constructor'da senkronize
  ediliyor (varsayılan: açık/görünür).
- `DesignCanvas.xaml`'a Bölüm 32'nin `SkElement`'inin yanına gerçek WPF `ScrollBar` kontrolleri
  (yatay+dikey) eklendi — bunlar `SKElement`'i BÜYÜTMÜYOR, sadece `_panOffsetX/Y`'yi okuyup/
  yazıyor (`UpdateScrollBars`/`OnHorizontalScrollBarScroll`/`OnVerticalScrollBarScroll`), Bölüm
  32'nin "kontrol boyutu asla zoom'a göre değişmesin" prensibini bozmadan sürükle-kaydır
  affordance'ı geri getiriyor. Thumb boyutu/pozisyonu dokümanın gerçek boyutu vs görünür viewport
  oranına göre hesaplanıyor (`Minimum/Maximum/ViewportSize/Value`), pan/zoom/resize sonrası
  `UpdateScrollBars()` çağrılarak güncel tutuluyor.

**Doğrulama:** build 0 hata (bir `using System.Windows.Controls` eksikliği hemen düzeltildi),
Core testleri 24/24 yeşil, uygulama yeniden başlatıldı ve çalışıyor.

## 34. Pencil ile çizerken 2-3 saniye gecikme — Bölüm 31'in `_bakedBitmap` önbelleklemesi PENCIL için bumerang oldu; artık sadece değişen pikseller yamanıyor

Kullanıcı: "masaüstündeki deneme.bmp dosyasını açıyorum çizim yapıyorum tıkladıktan 2-3 saniye
sonra görünüyor çizilmiyor kasılıyor" —  Neyse ki asıl
sorun Texcelle'in "sırrı" değil, Bölüm 31/32'de eklenen `_bakedBitmap` önbellekleme mekanizmasının
KENDİSİNİN yarattığı yeni bir performans regresyonuydu.

**Kök neden:** Bölüm 31, hücre-başı çizim yerine tüm dokümanı bir `_bakedBitmap`'e "pişirip" onu
tek bir ölçekli `DrawBitmap` ile çizmeye geçmişti — ama `_bakedBitmap`'i SADECE dokümanın GENEL
`DocumentChanged` olayında TAMAMEN YENİDEN İNŞA ediyordu. `DesignSurfaceViewModel.PaintPixel`
(pencil sürüklerken HER mouse-move'da çağrılıyor) da bu genel `DocumentChanged`'i tetikliyordu.
Sonuç: 240x600'lük bir tasarımda pencil ile sürüklerken, saniyede onlarca kez, HER SEFERİNDE
144.000 pikselin TAMAMI `SKBitmap.SetPixel` ile yeniden yazılıyordu — kullanıcının gördüğü
"tıkla, 2-3 saniye sonra görün" gecikmesi tam olarak buydu.

**Düzeltme:**
- `DesignSurfaceViewModel`'e yeni, dar kapsamlı bir olay eklendi:
  `event Action<IReadOnlyList<(int X, int Y)>>? PixelsChanged`. `PaintPixel` artık genel
  `RaiseDocumentChanged()` yerine SADECE gerçekten fırçanın dokunduğu pikselleri bu yeni olayla
  bildiriyor.
- `DesignCanvas.OnPixelsChanged`: `_bakedBitmap`'in TAMAMINI değil, SADECE bildirilen o birkaç
  pikseli `SetPixel` ile güncelliyor — pencil sürüklerken artık maliyet dokunulan piksel
  sayısıyla orantılı (birkaç, PencilSize kadar), doküman boyutuyla değil.
- `EndStroke()` hâlâ eskisi gibi genel `RaiseDocumentChanged()`'i çağırıyor (stroke bitince BİR
  KEZ) — bu, olası bir tutarsızlık ihtimaline karşı tam bir yeniden senkronizasyon sağlıyor
  (self-healing), ama artık sürüklemenin HER ADIMINDA değil sadece sonunda çalışıyor.
- Undo/Redo, Bucket, şekil araçları, seçim yapıştırma gibi diğer TÜM mutasyon yolları hâlâ genel
  `RaiseDocumentChanged()`'i kullanıyor (değişmedi) — bunlar zaten kullanıcı eylemi başına BİR
  KEZ çalışıyor, pencil'in aksine sürükleme sırasında tekrar tekrar değil, o yüzden tam yeniden
  inşa maliyeti burada sorun değil.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının gerçek deneme.bmp/Texcelle örneğinde pencil ile çizerek artık gecikme olmadığını
teyit etmesi gerekiyor.**

## 35. Gecikme Bölüm 34'ten SONRA da devam etti — kalan tek `RebuildBakedBitmap()` çağrısı (EndStroke, tıklama başına BİR KEZ) bile `SetPixel`'in kendisi yüzünden yavaştı; artık ham piksel byte buffer'ı + `Marshal.Copy` kullanıyor

Kullanıcı: "şuanda bile çizim yaparken kasma yaptığını ve gecikme yaptığı belli oluyor" — Bölüm
34'ün "sürüklerken artık sadece değişen pikseller güncelleniyor" düzeltmesi YETERSİZ kaldı,
çünkü `EndStroke()` hâlâ (doğru şekilde, sadece stroke bitince BİR KEZ) genel
`RaiseDocumentChanged()`'i çağırıyor, o da hâlâ TÜM `_bakedBitmap`'i `SKBitmap.SetPixel` ile
piksel piksel yeniden kuruyordu — ve `SetPixel`'in KENDİSİ, 144.000 çağrı için, tek başına
gerçekten yavaş. Kullanıcı ayrıca "Texcelle'in kodlarını yakalayabilir misin" diye sordu; netlik
için: hayır, derlenmiş bir programı decompile etmek/kırmak yapılmayacak/yapılamaz (lisans
ihlali) — ama kullanıcı bunu istemediğini, sadece daha önce paylaştığı `PixelBitmapControl.cs`
referansındaki YAKLAŞIMI (kendi yazdığı, meşru bir kod) örnek almamı istediğini netleştirdi.
O dosyanın kendi "PERFORMANS" yorumu zaten tam olarak bu dersi veriyordu: **"Bitmap.GetPixel/
SetPixel yerine ham piksel byte dizileri kullanılıyor... GetPixel çağırmaya gerek kalmıyor."**

**Somut ölçüm (ayrı bir bench projesiyle, Release, 240x600):**
- `SKBitmap.SetPixel` döngüsü: **326 ms**
- Ham `byte[]` buffer doldurup tek bir `Marshal.Copy` ile yazma: **~0 ms**

**Düzeltme:** `DesignCanvas.RebuildBakedBitmap` artık referans kontrolün `CacheArgbBytes`
metoduyla AYNI teknik: piksel piksel `SetPixel` çağırmak yerine, `bitmap.RowBytes` genişliğinde
bir `byte[]` buffer'ı düz döngüyle (yönetilen dizi indeksleme — çok ucuz) dolduruyor, sonra TEK
bir `Marshal.Copy(buffer, 0, bitmap.GetPixels(), buffer.Length)` ile bitmap'in native piksel
belleğine kopyalıyor. `OnPixelsChanged` (Bölüm 34'ün dar-kapsamlı yol) az sayıda piksel için
`SetPixel` kullanmaya devam ediyor — orada sorun yok, sadece birkaç piksel için çağrılıyor.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, ayrı bir bench projesiyle somut ölçüm
yapıldı (326ms → ~0ms). Uygulama yeniden başlatıldı ve çalışıyor. **Kullanıcının gerçek
deneme.bmp üzerinde tıklama/çizim yaparak artık gerçekten gecikme kalmadığını teyit etmesi
gerekiyor** — eğer hâlâ bir yavaşlık hissediliyorsa, bir sonraki şüpheli nokta muhtemelen
`OnPaintSurface`'in kendisi (yazılımsal `SKElement` render yolu) olur, o zaman `SKGLElement`'e
geçiş (Bölüm 32'nin sonunda önerilmişti) gündeme gelmeli.

## 36. Hızlı pencil çiziminde noktalı/boşluklu çizgi — Texcelle ile yan yana karşılaştırma sayesinde bulundu: MouseMove örnekleri arasında piksel enterpolasyonu eksikti

Kullanıcı gerçek Texcelle ve RugCAD ekran görüntülerini yan yana gösterdi: aynı hızlı, dairesel
kalem hareketiyle Texcelle'de kesintisiz bir çizgi çıkarken, RugCAD'de noktalı/boşluklu bir
çizgi çıkıyordu. Ayrıca "Texcelle'in kodlarını yakalayabilir misin" sorusuna netlik geldi —
kullanıcı programı kırmayı değil, daha önce paylaştığı kendi `PixelBitmapControl.cs`'sindeki gibi
MEŞRU bir yaklaşımı örnek almamı istiyor; bu kez konu performans değil, bir eksik özellikti.

**Kök neden:** WPF'in `MouseMove` olayı, hızlı bir fare hareketinde her design-pixel için ayrı
ayrı ateşlenmiyor — iki ardışık `MouseMove` örneği arasında imleç birden fazla design-pixel
atlayabiliyor. `DesignCanvas.OnMouseMove`'daki Pencil dalı sadece o anki `(x, y)`'yi boyuyordu;
aradaki atlanan pikseller hiç boyanmıyordu, bu da hızlı çizimde noktalı bir görünüme yol açıyordu.
Bu, RugCAD'e özgü bir bug — Texcelle muhtemelen aynı MouseMove aralığı sorununu, iki nokta
arasını bir çizgiyle (Bresenham) doldurarak çözüyor, ki bu zaten sektörde standart bir teknik.

**Düzeltme:** `DesignCanvas`'a yeni `_lastPencilPoint` alanı eklendi — pencil'in son boyadığı
design-koordinatını tutuyor. `OnMouseMove`'da artık `(x, y)`'yi doğrudan boyamak yerine,
`Rasterizer.Line(last.X, last.Y, x, y)` (zaten Line aracı için var olan Bresenham algoritması)
ile son nokta ile şimdiki nokta arasındaki TÜM pikselleri hesaplayıp hepsini `PaintPixel` ile
boyuyor — böylece iki `MouseMove` örneği arasında ne kadar mesafe atlanmış olursa olsun, çizgi
kesintisiz kalıyor. `_lastPencilPoint`, `OnMouseLeftButtonDown`'da stroke başlarken set ediliyor,
`EndStroke`'da `null`'a sıfırlanıyor (bir sonraki stroke'un ilk `MouseMove`'unda önceki stroke'un
son noktasından yanlışlıkla bir çizgi çekilmesin diye).

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının Texcelle ile yaptığı gibi hızlı, dairesel bir kalem hareketiyle RugCAD'de artık
boşluk kalmadığını teyit etmesi gerekiyor.**

## 37. BMP export gerçekte 24-bit true-color yazıyordu, Texcelle 8-bit indexed yazıyor — masaüstüne kaydedilen iki dosya (deneme.bmp vs denemetexcell.bmp) byte-byte karşılaştırılarak bulundu; GIF export de artık gerçek bir LZW encoder ile destekleniyor

Kullanıcı masaüstüne iki dosya kaydetti: `deneme.bmp` (RugCAD ile kaydedilmiş) ve
`denemetexcell.bmp` (Texcelle ile kaydedilmiş), "normalde bmp/gif kaydetmesi Texcelle gibi
kaydetmesi gerekir" diyerek. PowerShell ile iki dosyanın BMP header'larını byte byte karşılaştırdım:

| Alan | `deneme.bmp` (RugCAD, eski) | `denemetexcell.bmp` (Texcelle) |
|---|---|---|
| Dosya boyutu | 432.098 byte | 145.078 byte |
| BitCount | 24 (true-color) | **8 (indexed)** |
| PixelDataOffset | 54 (palet yok) | 1078 (54 + 256×4 renk tablosu) |
| XPelsPerMeter/YPelsPerMeter | 1890/2953 | 1890/2953 (aynı — Bölüm 31'de zaten doğru) |

**Kök neden:** `DesignDocument` zaten indeksli renk modelinde (palet + piksel başına 1 byte) —
tam olarak BMP'nin 8-bit indexed modunun istediği veri yapısı. Ama `ImageImportExportService.
Export`, BMP yazmadan ÖNCE dokümanı gereksiz yere 24-bit true-color bir `SKBitmap`'e
"düzleştiriyordu", bu yüzden RugCAD'in kendi BMP'leri Texcelle'inkinden ~3 KAT daha büyüktü ve
format olarak da farklıydı.

**Düzeltme — BMP:**
- `WriteBmp` (24-bit) tamamen `WriteIndexedBmp`'ye çevrildi — artık `SKBitmap` ara adımı hiç
  yok, doğrudan `document.Palette`/`document.GetPixel` kullanılıyor. BITMAPINFOHEADER'a
  `BitCount=8` yazılıyor, hemen ardından dokümanın paletinden (`Math.Min(256, Palette.Count)`
  girdi) bir BGR0 renk tablosu ekleniyor, piksel verisi artık 1 byte/piksel (4-byte hizalı
  satırlar, alttan yukarı) — Texcelle'in dosyasıyla YAPISAL olarak birebir aynı model.
- `Export`'un genel akışı da yeniden düzenlendi: BMP ve GIF artık en başta ayrılıp doğrudan kendi
  indeksli yazıcılarına gidiyor; sadece PNG/JPEG/WebP hâlâ true-color `SKBitmap` + SkiaSharp
  encode yoluna giriyor (bu üçü zaten indeksli-palet BMP/GIF tarzını desteklemiyor SkiaSharp'ın
  basit encode API'siyle).

**Düzeltme — GIF (artık gerçek export, sadece import değil):** Bölüm 27/29'da GIF export
"SkiaSharp encode edemiyor" diye bilinçli olarak KAPALI bırakılmıştı. GIF doğası gereği zaten
indeksli bir format olduğundan (paletimizle birebir uyumlu), gerçek bir LZW encoder yazıldı:
- `WriteGif`: GIF89a header + Global Color Table (GIF'in gerektirdiği 2'nin kuvveti boyutuna
  yuvarlanmış — 2,4,8,...,256) + tek bir Image Descriptor (tüm kanvası kaplayan) + LZW ile
  sıkıştırılmış piksel indeksleri (255-byte'lık alt-bloklar halinde) + trailer.
  `GifLzwEncode`: standart değişken-genişlikli GIF LZW algoritması (clearCode/endCode, prefix+byte
  çiftleri için büyüyen bir kod tablosu, 12-bit'e ulaşınca sıfırlama) — sıfırdan yazıldı, GIF
  spesifikasyonunun kendisi (RFC değil ama fiilen endüstri standardı) takip edildi.
- `MainWindow`'un `SaveAsFilter`'ına GIF geri eklendi (Bölüm 27'de "sadece import, fake olur"
  diye bilinçli olarak çıkarılmıştı — artık gerçek bir encoder olduğu için bu gerekçe geçersiz).

**Doğrulama (somut, ayrı bir prob projesiyle):** gerçek Texcelle BMP'si (`denemetexcell.bmp`)
`ImageImportExportService.Import` ile yüklenip (240x600, 9 renkli palet), hem yeni indeksli
BMP'ye hem yeni GIF'e export edildi, sonra HER İKİSİ de SkiaSharp'ın kendi `SKBitmap.Decode`'uyla
geri okunup orijinal piksellerle TEK TEK karşılaştırıldı:
- BMP: 144.090 byte (Texcelle'in 145.078 byte'ına çok yakın), density geri okunduğunda `(48, 75)`
  — birebir doğru, TÜM pikseller eşleşti.
- GIF: 27.401 byte (LZW sıkıştırması sayesinde BMP'den bile küçük), SkiaSharp başarıyla decode
  etti, TÜM pikseller eşleşti.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının UI üzerinden gerçek bir Save As → BMP ve Save As → GIF deneyip, oluşan dosyaları
Texcelle'de (veya başka bir görüntüleyicide) açarak doğrulaması gerekiyor** — otomatik dosya
diyaloğu testi hâlâ mümkün değil, ama alttaki yazıcı/okuyucu mantığı somut olarak doğrulandı.

## 38. Zoom out artık viewport merkezine, zoom in mouse'a anchor'lı; yön tuşları ile pan eklendi

Kullanıcı: "deseni uzaklaştırırken canvasın ortasına olucak şekilde uzaklaştır. yakınlaştırma
yapılırkende mouse bulunduğu noktaya yakınlaşsın. ve left,up,down,right yön tuşlarıda desen pan
ile sağ sol yukarı aşağı yaptırsın."

- **`DesignCanvas.OnPreviewMouseWheel`**: Ctrl+tekerlek ile zoom IN hâlâ mouse imlecine anchor'lı
  (Bölüm 32'den beri olduğu gibi — imlecin altındaki tasarım koordinatı zoom sonrası yine
  altında kalıyor). Zoom OUT artık FARKLI bir anchor kullanıyor: mouse pozisyonu yerine
  `SkElement`'in kendi viewport merkezi (`ActualWidth/2, ActualHeight/2`) anchor noktası olarak
  kullanılıyor — aynı `designXBeforeZoom`/`designYBeforeZoom` + sonradan `_panOffsetX/Y`'yi
  yeniden hesaplama mantığı, sadece anchor noktası `zoomingIn` bool'una göre seçiliyor.
- **Yön tuşlarıyla pan**: yeni `OnSkElementKeyDown` handler'ı — Left/Right/Up/Down tuşları
  `_panOffsetX/Y`'yi 40 birim kaydırıyor (referans `PixelBitmapControl`'ün kendi `OnKeyDown`'ındaki
  `step=40` ile aynı büyüklük). `SkElement`'e `Focusable="True"` eklendi ve
  `OnMouseLeftButtonDown`'a `SkElement.Focus()` eklendi (WPF'te klavye olaylarının gelebilmesi
  için elementin gerçekten focus'u alması gerekiyor — tıklamadan hemen sonra çalışsın diye).

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının zoom out'un canvas merkezine, zoom in'in mouse'a göre yakınlaştığını, ve
canvasa tıkladıktan sonra yön tuşlarının pan yaptığını UI üzerinden teyit etmesi gerekiyor.**

## 39. Zoom out minimumu artık sabit %25 değil, viewport'a göre dinamik — büyük bir desen artık gerçekten canvasa sığacak kadar uzaklaştırılabiliyor

Kullanıcı: "zoom out ve in maximum ve minumumları desenin yüzdelik oranına değilde farklı şekilde
hesapla çünkü mesela büyük bir desen açınca zoom out yapıyorum ancak canvasa sığmıyor boyutu."
Kök neden: `DesignSurfaceViewModel.MinZoom` sabit bir `const double = 0.25` (%25) idi — büyük bir
tasarımda (ör. 1000x1000) %25 zoom bile viewport'tan çok daha büyük kalabiliyordu, kullanıcı daha
fazla uzaklaşamıyordu.

**Düzeltme:**
- `DesignSurfaceViewModel.MinZoom` artık sabit değil, ayarlanabilir bir property (`_minZoom`
  alanı + `DefaultMinZoom = 0.25` sabiti hâlâ varsayılan/üst sınır olarak kullanılıyor). Set
  edildiğinde `ZoomLevel`'i yeni sınıra göre yeniden clamp'liyor.
- `DesignCanvas`'a yeni `UpdateZoomBounds()`: viewport boyutunu (`SkElement.ActualWidth/Height`)
  ve dokümanın gerçek boyutunu (Warp/Weft'ten gelen piksel en-boy oranı dahil, `PixelHeight`'ın
  kullandığı AYNI formül) kullanarak "tüm doküman viewport'a tam sığacak zoom seviyesi"ni
  (`fitZoom`) hesaplıyor, `MinZoom`'u `Math.Min(0.25, fitZoom * 0.9)` yapıyor — yani ya eski %25
  varsayılanı (küçük tasarımlarda hâlâ öyle) ya da (büyük tasarımlarda) dokümanı viewport'a
  gerçekten sığdıracak, %10 pay bırakan daha küçük bir değer, hangisi daha küçükse.
  `OnSkElementSizeChanged` (pencere/panel yeniden boyutlandığında), `OnDataContextChanged` (yeni
  doküman/sekme açıldığında) ve Warp/Weft/`UsePhysicalPixelAspect` değiştiğinde çağrılıyor —
  hepsi `fitZoom` hesabını etkileyen şeyler.
- Maximum zoom (`MaxZoom = 16.0`, %1600) DOKUNULMADI — kullanıcının şikayeti sadece zoom OUT'un
  büyük tasarımlarda yetersiz kalmasıydı, zoom in tarafında bir sorun bildirilmedi.

**Doğrulama:** build 0 hata, Core testleri 24/24 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının büyük bir tasarımda (veya pencereyi küçülterek viewport'u küçültüp) zoom out
yaparak artık tüm dokümanı görebildiğini teyit etmesi gerekiyor.**

## 40. Açılışta desen canvasa sığdırılıp ortalanıyor + YENİ: Design menüsü ve Resize Design diyaloğu (çoklu scale modu + crop/pad anchor)

Kullanıcı üç şey istedi: (1) "ilk desenin yüklenmesi veya açılması canvasa sığdırılmış ve
ortalanmış halde aç", (2) "menuye design ekle ve resize kısmını yapıcağız" (Texcelle'in Resize
Design diyaloğunun ekran görüntüsünü referans olarak paylaştı), (3) "scale image kısmına birkaç
farklı scale image modu ekleyeceğiz ki farklı şekillerde yeniden ebatlasın, amacımız
ebatlandırılırken daha az bozulması; bu mod Scale Image seçildiğinde seçilebilir olmalı, eğer
Scale Image aktif değilse default olarak kırpma/kesme yapılmalı."

**1) Açılışta fit + center:** `DesignCanvas.FitAndCenterView()` — viewport'a sığacak zoom'u
(Bölüm 39'daki `UpdateZoomBounds` ile AYNI formül, ikisi senkron kalsın diye) hesaplayıp
`SetZoom` ediyor, sonra `_panOffsetX/Y`'yi içeriği ortalayacak şekilde ayarlıyor. Yeni bir doküman
yüklendiğinde (`OnDataContextChanged`) tetikleniyor; ama yeni bir sekme henüz layout'tan
geçmediği için `ActualWidth/Height` o anda 0 olabiliyor — bu yüzden `_needsInitialFit` bayrağı
konup gerçek fit, viewport boyutu belli olunca (`OnSkElementSizeChanged`) TEK SEFER yapılıyor.

**2) Core: `RugCAD.Core.Drawing.DesignResizer`** (framework-bağımsız, 6 yeni birim testi —
Core testleri 24 → 30):
- `CropOrPad(source, newW, newH, ResizeAnchor, fillIndex)`: yeniden örnekleme YOK, mevcut
  pikseller orijinal boyutunu koruyor; kanvas büyürse yeni alan `fillIndex` ile doluyor,
  küçülürse kırpılıyor. `ResizeAnchor` 9 konum (TopLeft…BottomRight) — Texcelle'in anchor
  ızgarasının karşılığı. **Scale Image KAPALIYKEN varsayılan davranış bu** (kullanıcının açık
  talimatı).
- `Scale(source, newW, newH, ScaleMode)` — üç GERÇEK, farklı algoritma:
  - `NearestNeighbor` ("Normal"): her yeni piksel en yakın kaynak pikseli kopyalar; asla yeni
    renk uydurmaz, büyütmede en iyisi.
  - `Smooth`: en yakın 4 kaynak pikselin rengini bilinear harmanlayıp sonucu **paletteki en yakın
    renge** oturtur (indeksli bir dokümanda piksel yalnızca paletteki bir renk olabilir) —
    köşegen/eğri kenarlar daha yumuşak.
  - `AreaAverage`: her yeni pikselin kapsadığı TÜM kaynak pikselleri ortalayıp yine en yakın palet
    rengine oturtur; küçültmede en iyisi. Büyütürken ortalanacak alan olmadığı için otomatik
    olarak `NearestNeighbor`'a düşüyor.
- `CommandHistory.Clear()` eklendi (aşağıya bakınız).

**3) App: `Windows/ResizeDesignWindow.xaml(.cs)` + `_Design` menüsü.** `MainWindow`'a `_Design >
_Resize...` eklendi. Diyalog düzeni kullanıcının ikinci turdaki isteğine göre iki sütun:
**solda** "New Size" (Pixels X/Y + Keep Aspect Ratio) ve altında "Paper Format" (Warp/Weft,
düzenlenebilir), **sağda** "Resize Options" (Scale Image + üç mod radio'su, Scale Image kapalıyken
devre dışı) ve altında **3x3 anchor ızgarası** (liste/dropdown DEĞİL — kullanıcı açıkça
Texcelle'deki gibi ızgara istedi; `RadioButton`'lar özel bir `ControlTemplate` ile basılabilir
kare döşemeler olarak çiziliyor, ok glyph'leriyle). Anchor ızgarası Scale Image açıkken griye
düşüyor (resample edilirken anchor'ın bir anlamı yok).
- **Bilinçli kapsam sınırı:** Texcelle'in diyaloğundaki Unit dropdown'ı, fiziksel Size X/Y +
  Resolution X/Y alanları ve "Multiple Densities" tablosu EKLENMEDİ — ilk ikisi zaten Pixels ve
  Paper Format'ın başka birimle tekrarı, üçüncüsü ise ayrı ve büyük bir per-region density
  özelliği. Projenin "çalışmayan sahte kontrol koyma" ilkesi (Bölüm 15.3) gereği görünsün diye
  eklenmedi.

**4) `DesignSurfaceViewModel.ReplaceDocument(newDocument)`:** `Document` artık
`{ get; private set; }`. Resize yeni bir `DesignDocument` üretiyor (boyutlar kurucudan sonra
değişemiyor), bu metot onu yerine koyuyor; `History.Clear()` (kayıtlı her komut ESKİ boyutlara
göre piksel koordinatı tutuyor, geri alınırsa bozulur/patlar), `ClearSelection()` (maske/sınırlar
da eski boyuta bağlı), `RebuildPaletteEntries()` ve ilgili `PropertyChanged`/`DocumentChanged`
bildirimleri.

**Doğrulama:** build 0 hata, Core testleri **30/30** yeşil (6 yeni `DesignResizerTests`: anchor'lı
crop/pad, ortalama, kırpma, nearest-neighbor büyütme/küçültme, area-average tekdüze bölge).
Uygulama yeniden başlatıldı ve çalışıyor. **Kullanıcının UI'dan Design > Resize'ı gerçek bir
desende deneyip (hem Scale Image açık — üç modu da — hem kapalı/anchor'lı) sonucu teyit etmesi,
ve yeni açılan desenlerin gerçekten sığdırılmış+ortalanmış geldiğini doğrulaması gerekiyor.**

### 40.1 Resize diyaloğu iki sütuna ayrıldı, anchor ızgara oldu, kenar-başına dağıtım sayıları ve dolgu rengi seçimi eklendi

Kullanıcı aynı oturumda üç ek istek verdi, hepsi uygulandı:

1. **"resize da solda size ve altında paper format kısmı olsun, sağ taraf ise scale image ve
   altında anchor kısmı (texcelledeki olsun, liste şeklinde değil)"** — diyalog iki sütunlu
   `Grid`'e çevrildi: solda "New Size" (Pixels X/Y + Keep Aspect Ratio) ve altında "Paper Format"
   (Warp/Weft, düzenlenebilir — OK'a basınca ViewModel'e yazılıyor); sağda "Resize Options"
   (Scale Image + üç mod) ve altında "Anchor / Distribution". Anchor artık ComboBox DEĞİL,
   Texcelle'deki gibi **3x3 tıklanabilir döşeme ızgarası** (`RadioButton` + özel `ControlTemplate`,
   ok glyph'leri, seçili olan mavi).
2. **"anchorda sayı inputlar da ekle ki kaç kesileceğini de kullanıcı veya kendi belirlesin"** —
   ızgaranın altına, Texcelle'in "Distribution of new size" bloğu gibi haç düzeninde **dört sayı
   kutusu** (Left/Top/Right/Bottom) eklendi: her kenardan kaç piksel ekleneceğini (pozitif) veya
   kesileceğini (negatif) gösteriyor.
   - Anchor döşemesine tıklanınca bu dört kutu OTOMATİK doluyor
     (`DesignResizer.AnchorOffset` artık `public`, diyalog onu kullanıyor — anchor ile dağıtım
     aynı şeyin iki farklı ifadesi, iki ayrı kod yolu değil).
   - Kullanıcı bir kutuya elle yazarsa KARŞI kenar kalanı emiyor (`RebalanceHorizontal`/
     `RebalanceVertical`), böylece dördü her zaman gerçek boyut değişimini toplamıyor.
   - Pixels X/Y değişince dağıtım yeniden hesaplanıyor. Tüm bu çift yönlü senkron tek bir
     `_suppressSync` bayrağıyla korunuyor (aksi halde programatik yazmalar `TextChanged`
     üzerinden geri dönüp kullanıcının yazdığıyla çakışırdı).
   - OK'ta artık anchor değil, **kutulardaki Left/Top değerleri** kullanılıyor (kullanıcının elle
     girdiği sayı anchor'ı ezer). Bunun için `DesignResizer`'a açık offset alan yeni bir
     `CropOrPad(source, newW, newH, offsetX, offsetY, fillIndex)` overload'u eklendi; anchor'lı
     eski overload artık sadece offset'i hesaplayıp buna delege ediyor.
3. **"color seçme ekle anchor altına, eğer ekleme yapılacaksa rengi seçsin (paletteki)"** —
   anchor bloğunun altına **palet renk seçici** (`ComboBox`, her satırda renk kutusu + indeks +
   RGB) eklendi; kanvas büyüdüğünde yeni alan bu renkle doluyor. Genel bir RGB renk diyaloğu
   DEĞİL, çünkü indeksli bir dokümanda piksel yalnızca paletteki bir renk olabilir. Varsayılan
   olarak o an kanvasta seçili olan renk geliyor.

**Doğrulama:** build 0 hata, Core testleri 30/30 yeşil; ayrıca ayrı bir prob projesiyle açık
offset'li `CropOrPad` somut olarak doğrulandı — 4x4 siyah desen 8x8'e büyütülüp `left=3, top=1`
verildiğinde orijinal tam olarak x=3..6 / y=1..4'e oturdu, kalan alan seçilen dolgu rengiyle
doldu; negatif offset'le (kesme) 4x4 → 2x2 küçültmede doğru bölge korundu; `AnchorOffset`
TopLeft/Center/BottomRight için (0,0)/(2,2)/(4,4) döndü. Uygulama yeniden başlatıldı ve çalışıyor.

## 41. Palet neden 5-6 renk gösteriyordu: indeksli BMP'ler import'ta yeniden paletleniyordu — artık dosyanın KENDİ renk tablosu korunuyor; "sadece kullanılan renkler" filtresi ve undo'lanabilir resize eklendi

Kullanıcı üç şey bildirdi, üçü de aynı turda yapıldı:

**1) "palet 0'dan 255'e kadar, neden sadece 5-6 renk gösteriyorsun?"** — Kök neden:
`ImageImportExportService.Import` HER dosyayı SkiaSharp ile true-color'a decode edip, görüntüde
FİİLEN GEÇEN farklı renklerden sıfırdan minimal bir palet türetiyordu. Oysa 8-bit bir BMP zaten
indeksli — `DesignDocument` ile birebir aynı model — ve içinde genelde tam 256 girişlik bir renk
tablosu taşıyor (Bölüm 37'de Texcelle dosyasının `PixelDataOffset=1078` = 54 + 256×4 olduğu zaten
görülmüştü). Yeniden paletleme hem o tabloyu çöpe atıyor hem de indeksleri yeniden numaralıyordu —
bu alanda palet indeksi keyfi bir slot değil, **iplik/renk NUMARASI**.
- Yeni `TryImportIndexedBmp(filePath)`: sıkıştırılmamış 8-bit BMP'yi doğrudan okuyor — renk
  tablosunu (BGR0, `ColorsUsed` 0 ise 256 giriş) palet olarak, indeks baytlarını da piksel olarak
  AYNEN alıyor (pozitif `height` = satırlar alttan yukarı). Başka bir şeyse (true-color BMP, PNG,
  JPEG…) `null` dönüyor ve `Import` eski decode-ve-yeniden-paletle yoluna düşüyor.
- **Somut doğrulama (prob projesi):** Texcelle'in kaydettiği `denemetexcell.bmp` artık
  `palette=256 entries, used=9 distinct indices` olarak açılıyor (indeksler 1..9, orijinal
  numaralandırma korunmuş). Karşılaştırma için `MH1_B228A_P1611_0051.bmp` 9 girişle açılıyor —
  çünkü o dosya daha önce BİZİM uygulamamızdan kaydedilmişti ve `WriteIndexedBmp` paletin gerçek
  giriş sayısı kadar (`Math.Min(256, Palette.Count)`) yazıyor; yani round-trip tutarlı.

**2) "color palette içine checkbox ekle, aktif edildiğinde sadece desende kullanılan renkleri
göstersin"** — `DesignSurfaceViewModel.ShowOnlyUsedColors` eklendi; `RebuildPaletteEntries`
açıkken dokümanı bir kez tarayıp (`UsedPaletteIndices`) sadece gerçekten geçen indeksleri
listeliyor. `RaiseDocumentChanged` içinde (yalnızca filtre AÇIKKEN, çünkü tam piksel taraması
gerektiriyor) yeniden hesaplanıyor — bu olay kullanıcı eylemi başına bir kez tetikleniyor, canlı
kalem sürüklemesinde değil (o `PixelsChanged`'den geçiyor, bkz. Bölüm 34), dolayısıyla maliyeti
sorun değil. `PalettePanel.xaml`'a checkbox + 256 swatch'ı kaydırabilmek için `ScrollViewer`
eklendi.

**3) "resize ekranında yapılan işlemler undo/redo'ya kaydedilmiyor, tüm işlemler dahil edilecek"**
— Bölüm 40'ta `ReplaceDocument` bilinçli olarak `History.Clear()` çağırıyordu (gerekçe: eski
komutlar ESKİ boyutlara göre koordinat tutuyor). Bu gerekçe yeniden değerlendirildi ve YANLIŞ
olduğu görüldü: undo/redo kesinlikle LIFO, ve her komut kendi kaydedildiği doküman NESNESİNE
referans tutuyor — resize'dan önceki bir komut geri alınacağı ana gelindiğinde resize komutu da
zaten geri alınmış, yani o eski doküman yeniden canlı olan doküman. Dolayısıyla geçmişi tutmak
güvenli.
- Yeni `ViewModels/ReplaceDocumentCommand : IDesignCommand` — `before`/`after` doküman
  örneklerini tutuyor, `Do`/`Undo` hangisi güncelse onu ViewModel'e geri veriyor.
- `ReplaceDocument` artık `History.Execute(new ReplaceDocumentCommand(...))` çağırıyor; ortak
  uygulama yolu yeni `ApplyDocumentInstance` (palet yenileme + `PropertyChanged`/
  `DocumentChanged` bildirimleri). Seçim hâlâ resize öncesi temizleniyor (maske eski boyuta
  bağlı ve undo kapsamında değil).
- `CommandHistory.Clear()` artık kullanılmadığı için SİLİNDİ (ölü kod bırakılmadı).
- `DesignCanvas.OnDocumentChanged` artık `UpdateZoomBounds()` + `UpdateScrollBars()` de çağırıyor:
  bu olay artık sadece piksel içeriği değil, BOYUT değişimi de taşıyabiliyor (resize ve onun
  geri alınması).

**Doğrulama:** build 0 hata, Core testleri 30/30 yeşil, indeksli BMP import'u gerçek Texcelle
dosyasıyla prob edilerek doğrulandı (256 giriş + 9 kullanılan indeks). Uygulama yeniden başlatıldı
ve çalışıyor. **Kullanıcının UI'dan teyit etmesi gerekenler:** (a) Texcelle BMP'sini açınca
paletin artık 256 girişi göstermesi, (b) "Only used colors" checkbox'ının listeyi 9 renge
düşürmesi, (c) Design > Resize sonrası Ctrl+Z'nin resize'ı geri alması (ve Ctrl+Y'nin geri
getirmesi).

## 42. Palet dikey sıralamaya alındı + Photoshop tarzı "Colors" (foreground/background) alanı eklendi

**1) "paleti yukarıdan aşağı şekilde sırala, active paletin boyutuna göre olsun, yani enine
scroll olsun aşağı yukarı scroll olmasın"** — `PalettePanel.xaml`'daki `WrapPanel`'in
`Orientation`'ı `Horizontal` → `Vertical` yapıldı (swatch'lar yukarıdan aşağı gidip panelin altına
değince sağa yeni sütun açıyor), `ScrollViewer` da `HorizontalScrollBarVisibility="Auto"` +
`VerticalScrollBarVisibility="Disabled"` oldu. **Dikey scrollbar'ı kapatmak burada kozmetik değil
işlevsel bir gereklilik:** `WrapPanel`'in sütuna bölünmesi ancak yüksekliği viewport'a
sınırlandığında oluyor; dikey scroll `Auto` bırakılsaydı panel sonsuz yükseklik alır ve hiç
sarmazdı.

**2) "color palet üstüne Photoshop'taki gibi Colors alanı yapar mısın"** — paletin üstüne,
Photoshop'un düzeniyle aynı: sol-üstte foreground swatch'ı, onun arkasından sağ-altta background
swatch'ı ve yanında takas (⇄) düğmesi.
**Sahte bir gösterge olmaması için background rengi gerçek davranışlara bağlandı** (projenin
"çalışmayan kontrol koyma" ilkesi, Bölüm 15.3):
- `DesignSurfaceViewModel.BackgroundPaletteIndex` (yeni) + `ForegroundBrush`/`BackgroundBrush`
  (binding için) + `SwapColorsCommand`.
- **Sağ tuşla çizim background rengiyle boyuyor** (Pencil ve Bucket) — klasik pixel editor
  davranışı. `BeginStroke(byte paletteIndex)` ve `PaintPixels(points, byte paletteIndex)`
  overload'ları eklendi; `PaintPixel` artık `SelectedPaletteIndex` yerine stroke başlarken
  sabitlenen `_activeStrokeIndex`'i kullanıyor, böylece sürüş ortasında foreground değişse bile
  o stroke kendi rengiyle devam ediyor. `DesignCanvas`'a `OnMouseRightButtonDown/Up` eklendi
  (diğer araçlar sağ tuşu tamamen yok sayıyor).
- **Delete/seçim temizleme artık background rengiyle dolduruyor** — eskiden indeks 0'a hard-code
  edilmişti; `BackgroundPaletteIndex` varsayılanı da 0 olduğu için mevcut davranış aynen korunuyor,
  ama artık kullanıcı silme rengini seçebiliyor.
- Palet swatch'ına **sol tık foreground'u, sağ tık background'u** seçiyor
  (`PalettePanel.OnSwatchRightClicked`).

**Doğrulama:** build 0 hata, Core testleri 30/30 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının teyit etmesi gerekenler:** paletin dikey sıralanıp yatay kaydırması, foreground/
background swatch'ları, ⇄ takası, palet swatch'ına sağ tıkla background seçimi, kanvasta sağ
tuşla background rengiyle çizim, ve Delete'in background rengiyle silmesi.

## 43. Açılışta otomatik desen açılmıyor + File > New artık ayar diyaloğu soruyor + 256 renklik varsayılan palet

**1) "açılıştaki otomatik yeni desen açmasın"** — `MainWindow` kurucusundaki
`OpenDocumentTab(CreateDefaultDesign())` kaldırıldı; uygulama artık HİÇBİR doküman açık olmadan
başlıyor. Bunun yan etkileri de ele alındı: `OnActiveContentChanged`, son sekme de kapandığında
(`DocumentPane.Children.Count == 0`) `_activeViewModel`'i ve `DataContext`'i `null`'a çekiyor —
böylece yan paneller/toolbar kapanmış bir dokümanı canlı tutmuyor (binding'ler boşa düşüp komutlar
pasifleşiyor, doğru davranış).

**2) "new tıklayınca yeni desen ayarları sayfası açılsın ve kullanıcıdan ayarlar istesin"** —
yeni `Windows/NewDesignWindow.xaml(.cs)`: Name, Pixels X/Y, Paper Format Warp/Weft alanları +
OK/Cancel. `OnNewDesign` artık sabit 60x60'lık bir desen üretmek yerine bu diyaloğu açıyor;
iptal edilirse hiçbir şey açılmıyor. Varsayılan değerler kullanıcının gerçek dosyalarıyla uyumlu
seçildi (240x600, 48/75). İsim önerisi için oturum boyunca artan bir sayaç var (iptal edilirse
sayı yakılmıyor). Eski `CreateDefaultDesign()` tamamen silindi (ölü kod bırakılmadı), bununla
birlikte `MainWindow`'da artık kullanılmayan `using RugCAD.Core.Models` de kaldırıldı.

**3) "default bir palet ayarla 0-255'e kadar farklı renklerde"** — yeni
`Palette.CreateDefault256()` (Core): tam 256 giriş, hepsi birbirinden FARKLI renk.
- indeks 0 = beyaz (aynı zamanda varsayılan silme/background rengi, bkz. Bölüm 42),
  indeks 1 = siyah,
- 2..215 = standart 6x6x6 RGB küpü (51'in katları; siyah/beyaz zaten eklendiği için atlanıyor),
- 216..255 = ince gri rampa; adımları 255/41'in katları olduğu için küpün kendi grileriyle
  (51'in katları) HİÇ çakışmıyor — 256 girişin tamamı benzersiz kalıyor.
- Yeni `PaletteTests` (2 test): 256 giriş ve 256 farklı renk olduğu + 0'ın beyaz, 1'in siyah
  olduğu doğrulanıyor. **Core testleri 30 → 32.**

**Doğrulama:** build 0 hata, Core testleri 32/32 yeşil, uygulama boş (dokümansız) durumda
sorunsuz açılıyor — çalıştırılıp log'u kontrol edildi, hata yok. **Kullanıcının teyit etmesi
gerekenler:** açılışta boş gelmesi, File > New'in ayar diyaloğunu açması ve girilen ölçülerle
desen oluşturması, yeni desenin palet panelinde 256 rengi göstermesi.

## 44. KRİTİK düzeltme: resize artık desenin renklerini/paletini değiştiremiyor + iki yeni scale modu (Dominant / Preserve Detail) + kanvastan grid kaldırıldı

**1) "resize'da mevcut desenin paletini ve renklerini değiştirme — mesela AreaAverage ile scale
yapınca desen çok farklı bir şeye dönüyor, palet ve bazı yerler birbirine karışıyor"**

Kök neden — Bölüm 40'ta yazılan `NearestPaletteIndex`, harmanlanmış (ortalanmış/bilinear) rengi
**TÜM PALETTE** en yakın girişe oturtuyordu. Bölüm 41 ve 43'e kadar bu görece zararsızdı, çünkü
paletler zaten sadece desende geçen renkleri içeriyordu. Ama artık:
- indeksli BMP import'u dosyanın tam 256 girişlik tablosunu koruyor (Bölüm 41), ve
- yeni desenler 256 renklik varsayılan paletle geliyor (Bölüm 43).

Yani iki desen renginin ortalaması, desende HİÇ KULLANILMAYAN üçüncü bir palet girişine
oturabiliyordu — kullanıcının gördüğü "renkler birbirine karışmış, palet değişmiş" tam olarak
buydu. **Bu, Bölüm 41/43 değişikliklerinin fark edilmemiş bir yan etkisiydi.**

Düzeltme: yeni `UsedColors(source)` + `NearestUsedIndex(...)` — harmanlama modları artık yalnızca
**kaynak desenin FİİLEN kullandığı renkler** arasından seçiyor. Palet ne kadar büyük olursa olsun
resize asla desende olmayan bir rengi içeri sokamıyor.
- Bunu kalıcı olarak kilitleyen yeni bir `[Theory]` testi eklendi: 256 renklik palet içindeki
  2 renkli bir desen, BEŞ modun her biriyle küçültülüyor ve çıktının yalnızca o iki indeksi
  içerdiği doğrulanıyor.

**2) "birkaç scale image modu daha ekle, BMP'de scale yaparken çiftleme gibi şeyleri önlesin"** —
iki yeni mod (`ScaleMode` artık 5 mod):
- **`Dominant`** ("Dominant (no doubling, keeps colors)"): her yeni piksel, kaynak ayak izinin
  EN ÇOK hangi renkle kaplı olduğunu sayıp onu alıyor (çoğunluk oyu). Harmanlama yok → palet
  birebir korunuyor. NearestNeighbor'ın tek bir keyfi örnek pikseli seçmesi, tam sayı olmayan
  ölçek oranlarında satır/sütunların düzensiz çiftlenmesine yol açan şeydi; çoğunluk oyu bunu
  ortadan kaldırıyor.
- **`PreserveDetail`** ("Preserve Detail (keeps thin lines)"): aynı ayak izi mantığı, ama birden
  fazla renk varsa desende GENELİNDE EN NADİR olan kazanıyor. Tek piksellik konturlar/ince
  detaylar ağır küçültmede yok olmak yerine korunuyor (bedeli: seyrek gürültü de aynı şekilde
  korunur — doc comment'te açıkça yazılı).
- İkisi de tek bir `ScaleByVote(source, result, preserveDetail)` metodu ile uygulandı; büyütmede
  ayak izi tek piksele indiği için doğal olarak nearest-neighbor gibi davranıyorlar (özel durum
  koduna gerek kalmadı). İkisi için de ayrı birim test eklendi (çoğunluk seçimi, ve nadir rengin
  korunması). **Core testleri 32 → 39.**

**3) "desen görüntüsündeki grid görünümünü kaldıralım, grid'i ileride View içerisine
ekleyeceğiz"** — `DesignCanvas.OnPaintSurface`'teki ızgara çizimi ve `MinPixelSizeForGrid` sabiti
tamamen kaldırıldı (ölü kod/bayrak bırakılmadı; ileride View menüsüne toggle olarak eklenecek —
o zaman `CanvasViewOptions`'a Bölüm 33'teki Scrollbars toggle'ıyla aynı desende bir `ShowGrid`
eklenmesi doğal yol olur).

**Doğrulama:** build 0 hata, Core testleri **39/39** yeşil, uygulama yeniden başlatıldı ve
çalışıyor. **Kullanıcının teyit etmesi gerekenler:** AreaAverage/Smooth ile küçültünce artık
paletin/renklerin bozulmaması, yeni Dominant ve Preserve Detail modlarının sonuçları, ve kanvasta
artık grid çizgilerinin olmaması.

## 45. "Area, Preserve ve Dominant streçlemede tamamen aynı sonucu veriyor" — haklı: hepsi KÜÇÜLTME stratejisiydi; büyütme için EPX/Scale2x (EdgeSmooth) eklendi

Kullanıcı bir deseni streçleyip (BÜYÜTÜP) üç modu da denedi ve "hiçbir fark yoktu" dedi.
**Şikâyet tamamen doğruydu ve sebebi tasarımsaldı:** büyütmede her hedef pikselin kaynak ayak izi
TEK bir piksele düşüyor — bir pikselin çoğunluk oyu da, nadirlik oyu da, ortalaması da o pikselin
kendisidir. Yani Bölüm 44'te eklenen üç modun üçü de (+ AreaAverage'ın zaten var olan
NearestNeighbor'a düşmesi) büyütmede matematiksel olarak nearest-neighbor'a çöküyordu. O modlar
küçültme için tasarlanmıştı; büyütme için hiçbir şey yoktu.

**Araştırma (kullanıcının önerisiyle, web):** pixel-art büyütme algoritmaları ailesi incelendi —
EPX/Scale2x, Eagle, hq2x/3x/4x, xBR/xBRZ. Bu projede belirleyici kriter: **indeksli bir desende
yeni renk ÜRETİLEMEZ** (Bölüm 44'ün tam da düzelttiği sorun). Bu kritere göre:
- **hq2x / xBR / xBRZ: UYGUN DEĞİL** — interpolasyon yapıp yeni ara renkler üretiyorlar.
- **Eagle: uygun ama kusurlu** — kontrast arka plandaki tek piksellik detaylar KAYBOLUYOR
  (belgelenmiş bir kusur); halı desenlerinde tek piksellik konturlar kritik olduğu için elendi.
- **EPX / Scale2x (Eric Johnston, LucasArts ~1992 / Andrea Mazzoleni): SEÇİLDİ** — her pikseli
  kendi komşularından genişleterek köşegen merdivenini yuvarlıyor, harmanlama YOK, dolayısıyla
  sadece zaten var olan renkleri kullanıyor. Ayrıca hq2x'ten ~10x, xBR'den ~20x hızlı.

**Uygulanan:** yeni `ScaleMode.EdgeSmooth` — gerekli sayıda EPX ikiye katlama (`ApplyEpx`, en fazla
3 geçiş = 8x; hedefe ulaşınca duruyor) ardından tam istenen ölçüye `Dominant` geçişiyle iniş.
EPX kuralları yayınlanmış algoritmanın birebir kendisi (A/B/C/D = üst/sağ/sol/alt komşular):
`1=A if C==A && C!=D && A!=B`, `2=B if A==B && A!=C && B!=D`, `3=C if D==C && D!=B && C!=A`,
`4=D if B==D && B!=A && D!=C`. Bilinen bedeli doc comment'e yazıldı: 90° köşeleri ve üçgen/ok
uçlarını yuvarlıyor.

**Somut kanıt (prob, 8x8 köşegen kenar → 16x16):**
```
NearestNeighbor            EdgeSmooth (EPX)
##..............           ##..............
##..............           ###.............
####............           ###.............
####............           #####...........
```
Nearest 2 piksellik kaba merdiven üretiyor, EPX 1 piksellik ince merdiven — ve yalnızca
orijinal iki rengi kullanarak.

**Testler (Core 39 → 44):**
- `Scale_EdgeSmooth_DiffersFromNearestNeighborWhenEnlargingADiagonal` — EPX'in gerçekten farklı
  sonuç ürettiğini kilitliyor.
- `Scale_DownscalingModes_MatchNearestNeighborWhenEnlarging` [Theory] — Dominant/PreserveDetail/
  AreaAverage'ın büyütmede nearest-neighbor ile AYNI olduğunu **bilinçli davranış olarak**
  belgeliyor (ileride biri "bu bir bug mu?" diye bakarsa cevap testte yazılı).
- Bölüm 44'ün "yeni renk üretme" [Theory]'sine EdgeSmooth da eklendi.

**UI:** Resize diyaloğundaki mod listesi amaca göre gruplandı — üstte "Normal" ve
**"Edge Smooth — for ENLARGING"**, altında "For shrinking:" başlığıyla Dominant/Preserve Detail/
Smooth/Area Average, en altta da küçültme modlarının büyütmede neden Normal gibi davrandığını
açıklayan bir not (kullanıcının yaşadığı şaşkınlığın UI'da da cevaplanması için).

**Doğrulama:** build 0 hata, Core testleri **44/44** yeşil, uygulama yeniden başlatıldı ve
çalışıyor. **Kullanıcının teyit etmesi gereken:** aynı deseni streçlerken Normal ile Edge Smooth
arasında artık gözle görülür fark olması.

Kaynaklar: [Pixel-art scaling algorithms (Wikipedia)](https://en.wikipedia.org/wiki/Pixel-art_scaling_algorithms),
[Hqx (Wikipedia)](https://en.wikipedia.org/wiki/Hqx_(algorithm))

## 46. Menü adları temizlendi + Edit > Keyboard Shortcuts (kullanıcı tarafından düzenlenebilir)

Menü başlıklarındaki `_` karakterleri kaldırıldı. Edit > Keyboard Shortcuts... penceresi
49 komutu listeliyor: dosya/düzenleme/görünüm/panel komutları, çizim araçları, renk takası,
Only Used Colors, Paper Format, araç seçenekleri ve yön tuşlarıyla pan.
Arama, tuşa basarak atama, kaldırma, çakışmayı engelleme, varsayılanları geri getirme ve
OK/Cancel mevcut. OK ayarları `%AppData%/RugCAD/shortcuts.json` dosyasına atomik olarak
kaydeder; yükleme geçersiz veya çakışan dosyalarda varsayılanları korur. Menülerin kısayol
etiketleri değişikliklerden sonra güncellenir.

Tek kayıt listesi `MainWindow.Shortcuts.cs`; yeni komutlar buraya eklenmeli. Eski sabit
Window.InputBindings / ApplicationCommands bağlantıları kaldırıldı; atanmış tuşlar
InputManager üzerinden ana pencere ve AvalonDock floating pencerelerinde işlenir.
Metin alanlarında yazı/clipboard/navigation normal çalışır. Çizim sürerken kısayol çalışmaz;
Ctrl/Shift/Alt ile şekil/seçim davranışları ve fareyle zoom/pan aynı kalır.
Varsayılan araç tuşları Texcelle'e yakın: A Pencil, K Selection, D Eyedropper, F Bucket,
L Line, C Ellipse; X renk takası. Save As Ctrl+Shift+S, kısayol penceresi Ctrl+Alt+Shift+K.

Doğrulama: App build başarılı; Core 44/44 geçti. Ayrı STA WPF probu ana pencere ve kısayol
penceresini oluşturdu; 49 kaydın kimlikleri/varsayılan tuşlarının benzersizliği, liste,
Pencil araması ve tuş biçimlendirme/Windows rezervasyonu doğrulandı. Gerçek klavye
atama/kaydet/yeniden açma ve floating pencere etkileşimi henüz manuel teyit edilmedi.
Açık uygulama exe/dll dosyalarını kilitlediği için güncel derleme
`src/RugCAD.App/bin/ShortcutsPreview` klasöründe üretildi. Standart Debug çıktısının
güncellenmesi uygulama kapatıldıktan sonra normal build gerektirir.

## 47. Resize girişlerinde otomatik metin seçimi + anchor çevresinde sayısal dağıtım

Resize penceresi ContentRendered olduğunda Pixels X odaklanıp tüm metni seçiyor.
Penceredeki tüm TextBox alanları klavye/Tab odağında ve ilk fare tıklamasında SelectAll
uyguluyor; zaten odaklı alanda sonraki tık normal imleç yerleştirmeyi koruyor.
Top/Left/Right/Bottom dağıtım alanları ayrı alttaki haçtan anchor 3x3 ızgarasının
üst/sol/sağ/altına taşındı. Yeni NumericTextBox pozitif/negatif tamsayı girişini,
yazma ve paste doğrulamasını, Up/Down ile birer artırma/azaltmayı destekliyor.
Boş veya tek eksi geçici girişe izin veriliyor, OK tüm dört değeri ve toplamları
doğruluyor. Karşı kenar hesapları long kullanarak integer overflow önlüyor.
Doğrulama: App build başarılı (0 hata); manuel Tab/fare odağı ve yerleşim teyidi bekliyor.
Güncel exe: src/RugCAD.App/bin/ShortcutsPreview/RugCAD.App.exe.
Son doğrulama sırasında ShortcutsPreview oturumu açık olduğundan en güncel tam çıktı
src/RugCAD.App/bin/ResizePreview/RugCAD.App.exe yolunda üretildi (build 0 hata).

## 48. Anchor düğmeleri Tab sırasından çıkarıldı
AnchorTileStyle içine IsTabStop=False eklendi; tüm dokuz anchor düğmesi Tab/Shift+Tab
geçişlerinde atlanır, fareyle seçilebilir. Build 0 hata.
Güncel çıktı: src/RugCAD.App/bin/AnchorPreview/RugCAD.App.exe.

## 49. Ovalin piksel sınırları düzeltildi + Tools > Drawing Tools

EllipseOutline/EllipseFilled/EllipseOutlineConnected yarıçapları artık kapsanan piksel
alanından (inclusive width/height) hesaplanıyor. Önceki merkezler arası mesafe çift
ölçülerde dış kenarları eksiltiyor, 2x2 ovalde boş çıktı veriyordu. Tek satır/sütun
şekilleri görünür kalır. Pixel Cord aynı kuralı korur; normal kontur silinmez veya
kaydırılmaz, yalnızca köprü pikselleri eklenir.
Shift geometrisi ekranın PixelWidth/PixelHeight oranını kullanıyor: Paper Format
kapalıyken de ekranda kare/daire elde ediliyor. Normal ve Ctrl-merkezli çizimde gerçek
piksel sayıları (+1 inclusive sınır) hesaba katılır.
Tools > Drawing Tools mevcut 7 aracı SelectToolCommand ile seçer. Kısayol listesi
isimleri menü yoluyla eşleşir; sabit tool kimlikleri korundu, kullanıcı atamaları korunur.
Test: önce yeni çift ölçü testlerinin 5'i eski kodda başarısız oldu; düzeltmeden sonra
Core 55/55 geçti. Yeni testler çift ölçü kenarları/simetri, tek satır/sütun ve Pixel
Cord simetrisi/kontur korumasını kapsıyor. App build 0 hata. Kullanıcının istediği
ovalin görsel biçimi gerçek UI örneğiyle henüz teyit edilmedi; eski çizilmiş pikseller
bu düzeltmeyle geriye dönük değişmez, yeni oval çizilerek denenmeli.
Güncel tam çıktı: src/RugCAD.App/bin/OvalPreview/RugCAD.App.exe.

## 50. Kullanıcı kararı: yalnızca standart Debug çıktısı

Kullanıcı bin/Debug/net8.0-windows içindeki uygulamayı kullanıyor. Bundan sonra
farklı Preview isimli çıktı klasörleri oluşturulmayacak; standart dotnet build
çıktısı kullanılacak. Kilit varsa alternatif klasöre yönlenmek yerine açık
uygulamanın kapatılması sağlanmalı; kaydedilmemiş kullanıcı verisi zorla kapatılarak
kaybedilmemeli. Son oval ve Drawing Tools değişiklikleri standart Debug çıktısına
derlendi, build 0 hata.

## 51. Texcelle referansıyla çizim araçları genişletildi ve vektör ikonlar eklendi

Standart Debug çıktısı kullanıldı. 7 eski araca 16 yeni araç eklendi (toplam 23):
Elliptical Selection, Polygonal Selection, Lasso Selection, Select Figure, Pan,
Magnifying Glass, Brush, Airbrush, Eraser, Draw Text, Stamp, Contour, Gradient Fill,
Draw Polyline, Curve ve Draw Border.

DrawingToolCatalog tek araç kaynağıdır; aynı kayıtlar ikonlu toolbar, Tools > Drawing
Tools menüsü, kısayol listesi, tooltip ve Tool Options yardımını oluşturur. ToolIcon
orijinal WPF vektör çizimleridir; yeni görüntü/kütüphane bağımlılığı yoktur. Menülerin
özel şablonuna Icon sunumu eklendi. Toolbar aktif aracını tekrar tıklamak işaretini
kaldırmaz. Araç grupları Selection / Navigation / Paint / Fill / Motif / Shapes.

Brush/Eraser/Airbrush ayrı boyutlara sahiptir. Eraser arka plan palet rengiyle siler;
Airbrush boyut/yoğunluk ve 50 ms timer ile sabit farede de püskürtür. Sürekli çizim
tek stroke undo kaydıdır ve mevcut PixelsChanged önbellek yolunu kullanır. Yeni
boyama araçları seçimin maskesine uyar. Damga seçili motifin anlık görüntüsünü
kullanır (veya uygulama içi clipboard); farklı konumlara basılır, Alt-click mevcut
seçimden yeniden kaynak alır. Metin Arial ile Tool Options metni/boyutundan rasterize
edilir; alpha>=128 eşikleme seçili palet indeksini yazar, yeni renk üretmez. Degrade
seçili foreground/background indeksleriyle 4x4 Bayer dithering yapar, seçim varsa
orada çalışır. Bu sürümde Texcelle'in gelişmiş repeat-aware gradient modu yoktur.
Contour bağlı renk bölgesinin iç delikleri dahil sınırını boyar. Border sürüklenen
kutunun iç tarafına belirtilen kalınlıkta bordür çizer.

Seçimler mevcut bool maskesi/floating mimarisiyle çalışır: oval/lasso sürükleme,
poligon noktaları click; Select Figure bağlı aynı indeksli alanı click ile seçer.
Shift/Add, Alt/Subtract, Shift+Alt/Intersect desteklenir. Oval ve lasso seçimin içinde
sürükleme mevcut floating hareketini kullanır. Poligon/Polyline: double-click,
Enter veya sağ tık tamamlar; Backspace son noktayı siler; Escape iptal eder.
Curve: başlangıç, bitiş, sonra quadratic kontrol noktası olmak üzere 3 click.
Curve/Polyline kalınlıkları ayrı saklanır. Pan sol tuş sürükleme, Zoom click ve
Alt-click pointer'a anchor edilmiş büyütme/küçültme yapar.

Yeni varsayılan araç kısayolları: W Figure, H Pan, Z Zoom, B Brush, E Eraser,
G Gradient, T Text, S Stamp. Eski araç kimlikleri ve kaydedilmiş kullanıcı atamaları
korundu; yeni varsayılan bir tuş eski özel atamayla çakışırsa yalnızca yeni araç boş
bırakılır. Binding Points, Circular Copy, Duplicate Ellipse/Line, Mitre ve Insert/Remove
gibi uzman araçlar bu turda eklenmedi; yeni katalog mimarisi ileride bunları eklemeye hazır.

Doğrulama: Core 65/65 (10 yeni DrawingTools test vakası: konkav/clipped poligon,
polyline, kesintisiz quadratic curve, delik konturu, palet-korumalı gradient,
bordür kalınlığı ve yuvarlak fırça). Ayrı geçici STA/WPF probu 23 aracın menu/shortcut
kapsamasını ve ikon render'larını; stroke undo/redo, gradient, stamp, text,
curve/polyline işlemlerini, oval/poligon/lasso/figure seçimlerini, konturu, bordürü,
zoom/pan ve bağımsız yol ayarlarını doğruladı. İkon contact sheet görsel incelendi.
Gerçek kullanıcı fare akışlarının manuel teyidi hâlâ gerekli. App Debug build 0 hata;
restore cache'inde NU1900 ağ nedeniyle audit verisi alınamadı uyarısı mevcut.
Güncel uygulama src/RugCAD.App/bin/Debug/net8.0-windows/RugCAD.App.exe.

## 52. Select menüsü, Photoshop seçim davranışları ve Active Patterns (2026-09-18)

Bu bölüm eski floating seçimi bırakınca otomatik kopyalama kurallarının yerine geçer.
Normal seçim sürüklemesi yalnızca maskenin sınırını taşır. Move (V) veya Ctrl ile
sürükleme pikselleri taşır; Move+Alt/Ctrl+Alt kopyalar. Mouse-up işlemi hemen tamamlar;
deselect/yeni seçim/araç değişimi belgeye bekleyen kopya basmaz. Escape sürüklemeyi
iptal eder. Shift hareketi yatay/dikey/45 dereceyle sınırlar; ok tuşları 1 piksel,
Shift+ok 10 piksel ilerletir. Maske sınırı ve piksel taşıma Undo/Redo ile geri alınır;
örtüşen taşımalarda orijinal renkler ve seçimin şekli birlikte korunur. Taşınan alanın
tuval dışındaki kısmı kırpılır ve Undo ile geri getirilebilir; Shift bunun anahtarı değildir.

Select menüsüne All (Ctrl+A), Deselect (Ctrl+D), Reselect (Ctrl+Shift+D), Inverse
(Ctrl+Shift+I), Color Range, Modify/Border/Smooth/Expand/Contract/Feather, Grow,
Similar, Transform Selection, Show Selection Edges (Ctrl+H), Quick Mask (Q),
Load/Save Selection ve Add to Active Patterns eklendi. Edit'teki All/None buraya taşındı.
Araç seçeneklerinde New/Add/Subtract/Intersect seçilebilir. Shift ekler, Alt çıkarır,
Shift+Alt kesiştirir; seçim başladıktan sonra Shift kare/daire, Alt merkezden seçim,
Space çizilmekte olan kutuyu taşıma içindir. Seçim dışına pencil/shape/fill çizimi engellenir.
Rectangular Marquee varsayılanı M, Lasso L oldu; kaydedilmiş özel atamalar korunur.
Eski standart Escape/Deselect ve K/Marquee atamaları çakışmıyorsa yeni standartlara geçer.
Tüm yeni menü işlemleri Keyboard Shortcuts içinde düzenlenebilir.

Single Row/Column Marquee, Move ve Selection Brush ikonlarıyla eklendi. Selection
Brush seçim maskesini boyar; Alt/sağ tuş çıkarır. Magic Wand toleransı 0–255 ve
Contiguous seçeneği vardır. Quick Mask kırmızı örtü gösterir; Pencil/Brush/Eraser ile
siyah maskeler, beyaz seçer; belge pikselleri değişmez. Feather 0–255 coverage tutar;
üç ayrılabilir box blur ile Gaussian yaklaşımıdır. Marching ants yüzde 50 coverage
sınırını gösterir. Indexed belgede yarı seçili pikseller palet değiştirmeden ordered
dithering ile uygulanır; Photoshop RGB alpha blending ile birebir renk sonucu beklenmez.
Transform Selection konum/boyut/dönüşü yalnızca maskeye uygular ve soft maskeyi korur.
Save/Load Selection .rugselection dosyasında maske/coverage saklar; belge ölçüleri eşleşmelidir.
Uygulama içi clipboard sekmeler arasında çalışır ve farklı palete renk eşleştirir.

Active Patterns placeholder gerçek motif listesiyle değiştirildi. Seçim ismiyle
eklenir; şekil, soft coverage, indexed değerler ve kaynak RGB paleti snapshot olarak
%AppData%/RugCAD/patterns.json'a atomik kaydedilir. Liste tüm sekmelerde ortaktır ve
yeniden açılışta yüklenir. Double-click/Use as Stamp motifin kopyasını Stamp'e verir,
kaynak seçimi temizler; Remove listeden kaldırır. Hedef palette aynı indeks/renk
korunur; farklı palette en yakın RGB rengi kullanılır. IO hatasında liste değişikliği geri alınır.

Bu sürüm Photoshop'un bütün ürün kapsamını sağlamaz: katman/alpha-channel altyapısı,
Select Subject/Sky/Object gibi AI işlemleri, Select and Mask çalışma alanı, Magnetic
Lasso ve gerçek RGB blending yoktur. Color Range mevcut palette tam indeks eşleşmesi
seçer; Adobe'nin Fuzziness/skin-tone modeli değildir. Transform sayısal iletişim kutusudur,
Photoshop'un tutamaçlı serbest dönüştürme arayüzü değildir. Ctrl+J ile yeni katman üretme
mevcut tek yüzeyli belge modelinde hâlâ desteklenmez.

Doğrulama: Core 72/72 geçti; 7 yeni SelectionMasks testi kenar/hole contraction,
diagonal expansion, border, smoothing ve feather simetrisini doğrular. Geçici STA
doğrulaması 32 kontrolle menü/araç kapsamı, maske ve piksel taşıma, overlap/background,
Undo/Redo, reselect, implicit duplicate'in kalkması, çizim clipping, feather, Quick Mask,
Selection Brush, wand, soft motif, dönüş, palet JSON roundtrip ve sekmeler arası clipboard'u
doğruladı. Ek popup binding probunda test host'una özgü WPF startup/ResourceAssembly
hataları yakalanmadan dışarı çıktı ve verify.exe Windows hata pencereleri açtı; kullanıcı
bildirince geçici test süreçleri/dosyaları kaldırıldı. Bu probun ek kontrolleri başarılı sayılmadı.
Son gerçek App Debug derlemesi 0 hata / 0 uyarı; çıktı yine aynı
src/RugCAD.App/bin/Debug/net8.0-windows/RugCAD.App.exe. Kullanıcı açık uygulama için
taskkill izni verdi; son derlemede açık RugCAD süreci kalmamıştı, güncelleme başarılı oldu.

Davranış referansları (Adobe):
- https://helpx.adobe.com/photoshop/desktop/make-selections/refine-modify-selections/move-selection-or-selection-border.html
- https://helpx.adobe.com/photoshop/desktop/make-selections/refine-modify-selections/adjust-a-selection-manually.html
- https://helpx.adobe.com/photoshop/desktop/make-selections/refine-modify-selections/select-area-intersected-by-other-selections.html


## 53. ?ekil tu?lar? ve Active Patterns s?r?kle-b?rak (2026-09-18)

Rectangle/Ellipse/Border merkezden ?izimi Ctrl yerine Alt kullan?r; Shift fiziksel
kare/daire yapar, Alt+Shift merkezden kare/daire ?izer. ?nizleme ve mouse-up ayn?
k??eleri kullan?r; Border da ortak modifier hesab?na ba?land?. Ara? yard?mlar? g?ncellendi.

Se?im i?inden ba?layan ta??ma s?r?klemesinde pointer SkElement viewport'undan ??k?nca
WPF native DragDrop ba?lar. PatternDragData kaynak motifin palet/maske/coverage snapshot'?n?
ve tutuldu?u pikseli ta??r. Active Patterns ?zerine b?rakmak otomatik isimle kal?c? motif ekler;
kaynak tuval, se?im ve pikseller de?i?tirilmez. ?ptal/reddedilen drop kaynakta edit olu?turmaz.

Panelden motif s?r?klenebilir; thumbnail i?inde tutuldu?u piksel korunur, yaz?dan s?r?klemede
merkez anchor kullan?l?r. Tuval drop'u zoom/pan/physical pixel aspect'e g?re koordinat hesaplar,
renkleri hedef palette e?ler, maskenin bo?luklar?n?/soft coverage'?n? korur, bir undoable i?lemle
pikselleri yerle?tirir ve se?ili motifle Move arac?na ge?er. Drop ?ncesi hayalet ?nizleme vard?r;
belge d???na b?rakma reddedilir, kenara ta?an motif k?sm? belge s?n?r?na k?rp?l?r.
Active Patterns i?inde motifleri s?r?kleyerek s?ralama de?i?tirilebilir ve s?ra kaydedilir;
IO hatas?nda ekleme/s?ralama geri al?n?r. Bilinmeyen drag formatlar? mevcut file-drop yoluna b?rak?l?r.

Do?rulama: GUI host'u a?mayan, try/catch ile hatay? konsola d?nd?ren ge?ici console kontrol?
11 model kontrol?n? ge?ti: kaynak snapshot de?i?mezli?i, grab anchor, mask holes, selection/Move,
atomik undo/redo, drop sonras? hareket, farkl? palette RGB e?le?tirme, soft mask ve kenar k?rpma.
Kullan?c?ya verify.exe hata penceresi a?an ?nceki GUI test y?ntemi kullan?lmad?.
Son App build ayn? Debug klas?r?nde 0 hata/0 uyar?. Native fareyle panel-tuval drag gesture'lar?
otomatik masa?st? testiyle s?r?lmedi; kullan?c? aray?z?nde manuel teyit gerekir.


## 54. K?sayol oda?? ve motiften yeni sekme (2026-09-18)

Kullan?c? Active Patterns oda??ndayken Delete'in desendeki se?imi sildi?ini bildirdi.
MainWindow.DispatchShortcut art?k hem HandleDrawingKey (ok/Enter/Escape) hem de
?zelle?tirilebilir k?sayol dispatch ?ncesinde ActiveCanvas.HasDrawingFocus kontrol eder.
HasDrawingFocus yaln?zca SkElement.IsKeyboardFocusWithin'dir; aktif bir belge sekmesinin
varl??? tek ba??na klavye yetkisi de?ildir. Bu koruma t?m kaydedilmi? k?sayol atamalar?na
uygulan?r. Men?/t?klama komutlar? a??k kullan?c? i?lemleri olarak ?al??maya devam eder.

Active Patterns k?k? Focusable ve preview mouse-down ile bo? alan t?klamas?nda bile
eski canvas oda??n? b?rak?r. Yerel Delete, modifiers yokken se?ili motif i?in Remove/kal?c?
save kullan?r, e.Handled=true ile olay?n ba?ka yere gitmesini ?nler; key repeat silme yapmaz.
Palette/Tool Options/Paper Format i?in PanelKeyboardFocus ayn? bo? alan odak korumas?n?
uygular; child input/button/list kendi normal oda??n? mouse-down s?ras?nda al?r.

Son kullan?c? y?nlendirmesi motif double-click davran???n? Stamp yerine yeni belge sekmesi
olarak de?i?tirdi. ActivePattern.ToDocument ayn? piksel ?l??s? ve kaynak RGB paletiyle
ba??ms?z indexed belge ?retir; maske delikleri indeks 0 arka plan, soft coverage ordered
dithering ile uygulan?r. MainWindow.OpenPatternTab normal OpenDocumentTab ak???n? kullan?r;
yeni sekme aktif oldu?unda deferred Input ?nceli?inde ?izim y?zeyine odak verir.
Panelde Use as Stamp d??mesi ayr? i?lev olarak durur.

PatternDragData uygulama k?k?ndeki bo? alana veya mevcut tuvalin belge d???nda kalan
gri ?al??ma alan?na b?rak?l?nca yeni sekme a?ar. Ger?ek belge piksel alan?na drop h?l?
se?ili/movable motif yerle?tirir; panel drop h?l? kay?t/s?ralama yapar. Root file-drop yolu
korunur; canvas/panel handled drop'u ikinci defa root'ta i?lenmez. Uygulama d???ndaki
masa?st? ba?ka bir drop target oldu?undan bu yeni sekme davran??? yaln?zca RugCAD
?al??ma alan?nda ge?erlidir.

Do?rulama: App standart Debug derlemesi 0 hata/0 uyar?. GUI/Application host'u a?mayan,
try/catch ile hatay? konsola d?nd?ren ge?ici kontrol 7 belge kontrol?n? ge?ti: boyut,
piksel indeksleri, mask holes, kaynak paleti, kaynak k?t?phaneden ba??ms?zl?k, ba??ms?z
ikinci sekme ve soft coverage. Kal?c? motif/shortcuts dosyas?na test verisi yaz?lmad?.
Windows native keyboard focus/drag hareketleri otomatik masa?st? testiyle s?r?lmedi.
A??k uygulama kilidi i?in kullan?c?dan ?nceki taskkill izni kullan?ld?; sandbox eri?imi
reddedince require_escalated taskkill ba?ar?l? oldu ve ayn? Debug ??kt? g?ncellendi.

## 55. Büyük desen seçim performansı ve Curve modları

- Seçim değişiklikleri DocumentChanged yerine SelectionChanged bildirimi kullanıyor; seçim taşıma, gizleme, Quick Mask geçişi ve yalnızca seçim içeren undo/redo desen bitmap'ini yeniden oluşturmuyor.
- Seçim sınırları bitişik kenarları birleştirerek önbelleğe alınıyor. Büyük dikdörtgen dört çizgiye indirgeniyor; sınır sürükleme sırasında piksel capture yapılmıyor. Piksel taşıma önizlemesi capture bitmap'ini önbellekten çiziyor.
- Seçim geçmişi yalnızca sınır kutusundaki maskeyi bit paketleyerek saklıyor; feather coverage sınır kutusuna kırpılıyor. Dikdörtgen seçim doğrudan maske dolduruyor ve sınırları tekrar taramıyor. Sınır taşıma piksel değerlerini kopyalamıyor. Maske fırçasının tam sınır taraması stroke sonuna ertelendi.
- Quick Mask katmanı değişiklikler arasında bitmap önbelleğinden çiziliyor.
- Curve: Bézier, Spline (clamped B-spline), Spline through points (cardinal interpolation) ve 0–100 Roundness. Noktalar tıklanarak ekleniyor; çift tıklama, Enter veya sağ tık bitiriyor; Backspace son noktayı kaldırıyor; Escape iptal ediyor.
- Curve ve Polyline önizlemesi seçili palet rengi ve kalınlığıyla, uygulama ile aynı raster algoritmasından çiziliyor. Seçim kırpması da ortak.
- Doğrulama: 80 Core testi geçti; 4000×3000 modelde 3500×2500 seçim 62 ms (tek yerel ölçüm; ekran FPS ölçümü değildir). Seçim move/undo/redo/deselect/reselect ve Quick Mask geçişleri 0 DocumentChanged; piksel move/undo geçti. Geçici kontrol herhangi bir GUI host açmadan çalıştırıldı ve kaldırıldı.
- Standart çıktı src/RugCAD.App/bin/Debug/net8.0-windows/RugCAD.App.exe güncellendi; derleme hatasız. NuGet güvenlik verisi için ağ erişimi olmadığına ilişkin NU1900 uyarıları görüldü.

## 56. Curve etkileşimi ve piksel kalınlığı düzeltmesi

- Curve bağımsız Pen Size X/Y, Proportional (mevcut Warp/Weft SetLinkedSize kuralı) ve Pixel Cord / Running Cords ayarlarını kullanıyor. Önizleme ve uygulama aynı kalınlaştırma ve çapraz adım köprülemesini kullanıyor.
- Spline through points: tıklayarak noktalar eklenir; ilk Enter düzenlemeye geçirir ve tutamaçlar görünür; noktalar sürüklenir; ikinci Enter tek undo adımı olarak desene uygular. Düzenlemede fare hareketi yeni nokta eklemez.
- Spline: iki uç tıklanır; fare hareketi orta eğim noktasını belirler; bir tıklama veya Enter uygular. Üç nokta arasındaki interpolasyon Roundness ile ayarlanır.
- Bézier: iki uç tıklanır; iki kontrol kolu otomatik oluşturulur; kontrol noktaları veya uçlar sürüklenerek düzenlenir; Enter uygular. Kontrol kolları ve noktalar gösterilir. Roundness bu modda devre dışıdır.
- Escape ve araç/mod değiştirme geçici eğriyi iptal eder; sürükleme mouse capture kullanır; Enter auto-repeat ikinci aşamayı yanlışlıkla uygulamaz.
- Araç yardımı yeni üç akışı açıklıyor. Core 83 test geçti; kalınlık X/Y ve Running Cords bağlantı testleri eklendi. Standart Debug derlemesi güncellendi, hata yok. Masaüstü etkileşimleri otomatik GUI host ile çalıştırılmadı.

## 57. Active Pattern bırakma önizlemesi

- Desene pattern drop artık piksellere uygulanmayan dış kaynaklı FloatingSelection oluşturuyor; Move sürüklemesi ve oklarla tekrar tekrar taşıma yalnızca önizleme konumunu değiştiriyor.
- Dış kaynaklı önizleme altında arka plan silme bitmap'i çizilmiyor; cut/move kaynak silmesi uygulanmıyor.
- Enter tek undo işlemiyle son konuma uygular; Escape/Delete veya araç değiştirme önizlemeyi iptal eder. Eski desen seçimini taşıma davranışı korunur.
- Model kontrolleri: drop ve iki ardışık move tüm alt pikselleri korudu, history oluşturmadı; maskeli commit/undo/redo ve Delete iptali geçti. GUI host açılmadı. Standart Debug çıktısı güncellendi.

## 58. Curve Pixel Cord kapalı modda çapraz adımlar

- Sorun checkbox değil yoğun alt piksel örneklemesiydi: ayrı X/Y sınırı geçişleri, 1×1 kalınlıkta bile ortogonal köşe pikselleri oluşturuyordu.
- CurveRasterizer ham örneklemedeki yinelenen pikselleri ve tek hücreli fazladan köşe adımını temizler. Spline through points kullanıcı noktaları korunur.
- Temiz çapraz hat ortak önizleme/uygulama girdisidir. Pixel Cord açık olduğunda ConnectDiagonalSteps köprü piksellerini ekler; kapalı modda eklenmez.
- Üç mod için köşe pikseli regresyon testleri eklendi; uçlar ve açık/kapalı farkı doğrulandı. 86 Core testi geçti; standart Debug derlemesi hatasız güncellendi.

## 59. Curve Roundness ve görünür nokta tutamaçları

- Spline through points ilk Enter düzenleme durumunu açıyordu fakat DrawCurveHandles çağrısı eksikti. Tutamaçlar artık önizleme katmanlarından sonra çizilir; 10 px beyaz dolgu ve 2 px siyah kenar kullanır. Hit test gerçek fare ekran konumuyla yapılır.
- Points modunda çift tıklama düzenleme durumuna erken geçirmez; ilk Enter noktaları gösterir, ikinci Enter uygular. Böylece ilk Enter yanlışlıkla commit yapmaz.
- Interpolasyon teğetlerinin Roundness aralığı 4 kat genişletildi: %25 standart cardinal yumuşatma, yüksek değerler belirgin daha yuvarlak kıvrımlar. Bézier kontrol kolu davranışı korunur.
- Roundness düşük/yüksek farkı ve tıklanan noktaların korunması testi eklendi; 87 Core testi geçti. Standart Debug çıktısı hatasız güncellendi.

## 60. Curve düzenleme desteği ve taşınabilir Duplicate

- Curve nokta tutamaçları toplama aşamasında da görünür ve sürüklenebilir. Seçili nokta turuncudur; gerçek fare ekran konumuyla seçilir. Bézier kontrol kolları düzenlemede görünür.
- Curve noktası seçiliyken oklar 1 px, Shift+oklar 10 px ince ayar yapar; desen selection'ının ok işlemi engellenir. Delete selected points noktasını kaldırır (en az iki nokta korunur). Ctrl+click düzenlemede yakın kontrol noktası aralığına yeni nokta ekler.
- Tool Options: Edit points, Apply, Cancel, Remove point düğmeleri ve kullanım ipucu. Panel düğmeleri VM event üzerinden o desene ait canvas'a gider ve canvas focus döner. Apply points modunda fareye bağlı geçici nokta eklemez.
- Select > Duplicate Selection eklendi; mevcut Edit komutu ve Ctrl+J aynı davranışı kullanır. Aynı yere görünmez no-op damga yerine dış kaynaklı taşınabilir kopya preview oluşturulur; asıl pikseller korunur. Enter uygular, Escape iptal eder.
- Duplicate model testi: preview, move, commit, kaynak koruma, undo/redo geçti. Core 87 test geçti. Standart Debug çıktısı hatasız güncellendi. GUI host kullanılmadı.

## 61. Lasso silgi ve Active Pattern ile çizim

- Silgi varsayılanı Lasso area: basılı tutarak serbest alan çizilir, bırakınca alan background ile silinir. Brush radyo seçeneği eski yuvarlak silgiyi korur. Escape geçici lasso'yu iptal eder; seçim kırpması ve undo uygulanır.
- Tüm piksel üreten araçlara Paint from selected Active Pattern seçeneği eklendi (Pencil, Brush, Eraser, Airbrush, Line, Rectangle, Ellipse, Bucket, Text, Stamp, Contour, Gradient, Polyline, Curve, Border). Tercih araç bazında saklanır. Selection ve gezinme araçları bu piksel çizimi ayarını kullanmaz.
- Active Patterns liste seçimi PatternLibrary.Selected ortak motifini günceller. Motif mevcut palete eşlenir; canvas koordinatlarına sabit tekrar eden dolgu kullanılır. Maske boşlukları ve feather dithering alt pikselleri korur. Motif seçili değilse pattern modunda piksel uygulanmaz.
- Pattern dolgu tek renk PaintPixels ve canlı stroke uygulamalarına ortak olarak bağlandı; gradient de aynı yolu kullanır. Taşıma, duplicate, paste ve Delete gibi doğrudan pixel edit işlemleri pattern dolgudan etkilenmez. Quick Mask/Selection Brush kendi maske rengini korur.
- Shape, Curve, Polyline ve Border önizlemeleri aynı pattern renk/maske örneklemesini kullanır. Silgi pattern seçeneği açıksa lasso veya fırça alanına motif uygulanır.
- Model kontrolü: 15 araçta pattern/mask/undo; canlı pencil/brush; varsayılan lasso; lasso seçim kırpması/undo; eksik pattern kontrolü geçti. Core 87 test geçti. GUI host kullanılmadı. Standart Debug derlemesi hatasız güncellendi.

## 62. Lasso silgi canlı dolgu önizlemesi

- Lasso silgide hareket ederken kapalı polygon alanı gerçek background rengiyle canlı gösterilir; selected Active Pattern açıkken aynı motif/şeffaflık/selection clipping örneklemesi önizlemeye uygulanır. Document pikselleri mouse-up'a kadar değişmez; Escape önizlemeyi iptal eder.
- Core PolygonSpans kenarları dahil aynı polygon dolguyu yatay run olarak üretir; Polygon commit piksellerini de bu run'lardan üretir. HashSet ile tüm dolgu piksellerini biriktirme kaldırıldı.
- Önizleme görünür viewport ile kırpılır; seçim/pattern yokken her run tek DrawRect ile çizilir. Selection veya pattern varsa yalnızca görünür sınır kutusu bitmap olarak blit edilir. Quick Mask canlı dolgu bu değişikliğin kapsamında değildir; lasso sınırı görünmeye devam eder.
- İçbükey şekil/kenar ve viewport kırpmasının commit ile eşitliği, büyük rectangle'ın satır başına tek run üretmesi doğrulandı. 89 Core testi geçti. Standart Debug derlemesi hatasız güncellendi.

## Stamp Photoshop davranışı + Alt sağ-tık ile fırça boyutu

Kullanıcı: "stamp photoshopdaki gibi alt tuşuyla seçilip brush gibi boyasın, photoshopla aynı
olsun. ve photoshopdaki gibi alt mouse sağ tık ile size ayarlıyabilelim."

**1) Stamp neden Photoshop gibi davranmıyordu — kök neden:** klon mantığı (`CloneStamp` +
`DesignSurfaceViewModel.CloneStamp.cs`) zaten doğruydu: Alt+click kaynak seçiyor, `RoundBrush` ile
boyuyor, stroke boyunca `Rasterizer.Line` ile ara nokta dolduruluyor (boşluk yok), ve `_original`
sözlüğü sayesinde üst üste binen fırça darbeleri kaynağın DEĞİŞMEMİŞ halinden örnekliyor —
Photoshop'un yaptığının aynısı.

Sorun `DesignSurfaceViewModel.Selection.cs`'deki `UsePattern`'dı: bir Active Pattern seçildiğinde
`CurrentTool = Stamp; PaintFromActivePattern = true;` yapıyor. Bu bayrak `_patternPaintTools`
kümesinde **araç bazında kalıcı** tutulduğu için, kullanıcı bir kez pattern seçtikten sonra Stamp
aracı sonsuza dek "pattern damgalama" modunda kalıyor — `BeginExtendedTool`'daki klon bloğu
`!PaintFromActivePattern` koşuluyla korunduğundan **Alt+click hiçbir şey yapmıyordu**. Kullanıcının
"Photoshop gibi olsun" demesinin sebebi buydu.

Düzeltme (`DesignCanvas.DrawingTools.cs`): Stamp için Alt+click artık `PaintFromActivePattern`
kontrolünün ÖNÜNDE ele alınıyor ve o modu kapatıyor — Alt+click Photoshop'ta tartışmasız "klon
kaynağı tanımla" hareketi olduğu için, kullanıcıyı Tool Options'ta checkbox aramaya zorlamadan
aracı klon moduna geri döndürüyor. Pattern damgalama modu kaybolmadı; Alt'sız tıklamayla aynen
çalışmaya devam ediyor (blok artık fall-through ile genel damgalama yoluna düşüyor).

**2) Alt + sağ tık sürükle ile fırça boyutu (Photoshop hareketi):**
- `DesignSurfaceViewModel.DrawingTools.cs`: yeni `HasActiveBrushSize` (Brush/Eraser/Airbrush/Stamp)
  ve `ActiveBrushSize` — hangi fırça aracı aktifse onun boyutunu okuyup yazan tek nokta. Araçların
  kendi boyutları ayrı kalmaya devam ediyor (Photoshop'ta da her araç kendi boyutunu hatırlar).
- `DesignCanvas.DrawingTools.cs`: `TryBeginBrushResize` / `MoveBrushResize` / `EndBrushResize` +
  `DrawBrushResizePreview`. Alt basılıyken sağ tuşla sürüklerken yatay hareket boyutu değiştiriyor
  (4 ekran pikseli = 1 boyut adımı), kanvasta fırçanın gerçek ayak izi ve sayısal boyut canlı
  gösteriliyor. `OnMouseRightButtonDown`'da diğer tüm sağ-tuş davranışlarından ÖNCE devreye giriyor.
- **Photoshop'un dikey hareketi "hardness"a bağlaması bilinçli olarak alınmadı:** indeksli bir
  desende yumuşak kenar diye bir şey yok (`RoundBrush` her zaman sert), dolayısıyla hardness ayarı
  hiçbir şey yapmayan sahte bir kontrol olurdu (Bölüm 15.3 ilkesi).
- Tool Options'taki Stamp yardım metnine "Alt + right-drag resizes the brush" eklendi.

**Doğrulama:** build 0 hata, Core testleri 89/89 yeşil, uygulama yeniden başlatıldı ve çalışıyor.
**Kullanıcının teyit etmesi gerekenler:** (a) daha önce Active Pattern seçilmiş olsa bile Stamp'ta
Alt+click'in artık klon kaynağını belirlemesi ve fırça gibi boyaması, (b) Brush/Eraser/Airbrush/
Stamp araçlarında Alt + sağ tık sürüklemenin boyutu canlı değiştirmesi.

## Magic Wand: çift tıkla rengin tamamını seçme

Kullanıcı: "magic wand ile çift tıklandığında tüm rengi seçsin desendeki."

Magic Wand aracı `DrawTool.FigureSelection` (katalogdaki adı "Magic Wand"). Zaten bir
`WandContiguous` seçeneği vardı: açıkken tıklanan noktadan yayılan BİTİŞİK bölgeyi, kapalıyken
desendeki TÜM eşleşen pikselleri seçiyordu — ama bunun için kullanıcının Tool Options'a gidip
checkbox'ı kapatıp sonra geri açması gerekiyordu.

- `SelectWand`'a opsiyonel `bool? contiguous` parametresi eklendi; `contiguous ?? WandContiguous`
  şeklinde yalnızca O ÇAĞRI için ayarı geçersiz kılıyor (kalıcı ayara dokunmuyor).
- `DesignCanvas.DrawingTools.cs`, `FigureSelection` dalında `e.ClickCount >= 2` ise `false`
  geçiyor — yani çift tık her zaman global seçim yapıyor, Contiguous ne olursa olsun.
- Çift tıkta WPF önce `ClickCount=1` ile bir mouse-down gönderdiği için ilk tık bitişik bölgeyi
  seçiyor, ikinci tık (Replace modunda) onu global seçimle değiştiriyor — sonuç doğru. Shift/Alt
  ile birleştirme modları da bozulmuyor (global küme bitişik kümeyi zaten kapsıyor).
- Keşfedilebilirlik: `DrawingToolCatalog`'daki araç açıklamasına ve Tool Options'taki Magic Wand
  bölümüne çift tık davranışı yazıldı.

**Doğrulama:** build 0 hata, Core testleri 89/89 yeşil, uygulama yeniden başlatıldı ve çalışıyor.

## Bölüm 46 — Magic Wand ile tüm rengi seçince yaşanan kasma (marching ants darboğazı)

**Şikâyet:** `D:\YLDRMHASAN\DESENLER\C057\C057A_GRAY_N02.bmp` deseninde Magic Wand ile bir rengi
komple seçince program kasıyor.

**Ölçüm (tahmin değil):** Dosya 800×1500 = 1.200.000 piksel, 8-bit indexed. En baskın renk
402.497 piksel kaplıyor ve bu seçimin konturu **234.454 ayrı kenar parçası** üretiyor. Sebep
halının dithering'li dokusu: seçilen renk tek parça bir bölge değil, desenin her yerine serpilmiş
binlerce küçük ada.

**Kök neden:** Seçimin kendisini hesaplamak (O(W·H) ≈ 1,2M) hızlı. Darboğaz **çizim** tarafında:
`DrawCachedSelectionOutline` bu 234k parçayı bir `SKPath`'e koyup her karede **iki kez**
(gölge + beyaz çizgi) `DrawPath` ile çiziyordu ve her iki boya da `SKPathEffect.CreateDash`
taşıyor. Dash efekti her parçayı ayrıca alt parçalara böler; `SKElement` yazılım renderer'ı
olduğu için bu, her mouse hareketinde/pan/zoom'da saniyeler süren bir iş demek. Bölüm 31/32/35'te
canvas'ın kendisi için çözdüğümüz sorunun aynısı, bu kez seçim katmanında.

**Çözüm — iki kademeli marquee (`DesignCanvas.SelectionCache.cs`):**
- `DenseOutlineEdgeLimit = 20000`. Kontur bu sınırın altındaysa hiçbir şey değişmedi: eskisi gibi
  vektörel, kesik çizgili marching ants.
- Sınırın üstündeyse `DrawDenseSelectionOutline` devreye giriyor: seçim sürümü başına **bir kez**
  desen boyutunda bir `SKBitmap` rasterize ediliyor (seçili olup 4-komşusundan biri seçili olmayan
  her piksel sınır pikselidir), sonra her kare sadece tek bir nearest-neighbour blit'e mal oluyor.
  Okunabilirlik için sınır pikselleri `(x+y)` üzerinden siyah/beyaz dönüşümlü boyanıyor — dash
  deseninin yaptığı işi, yani hem açık hem koyu zeminde görünür kalmayı, statik olarak yapıyor.
- Bu yoğunlukta kesik çizgiler zaten görsel gürültüden ibaretti, dolayısıyla okunabilir bir bilgi
  kaybedilmiyor.
- Aynı mantık `DrawCachedCapture` için de uygulandı: global bir seçimden yüzen (floating) parça
  oluşturulduğunda konturu aynı ölçüde parçalı olur; orada kontur çizimi atlanıyor, çünkü parçanın
  şeklini zaten capture bitmap'inin kendisi gösteriyor.
- `ResetSelectionCache` yeni bitmap'i de temizliyor; cache anahtarı yine `SelectionVersion`.

**Doğrulama:** build 0 hata, Core testleri 89/89 yeşil, uygulama yeniden başlatıldı (PID 34652).

## Bölüm 47 — Gradient Fill: gradient türleri + multicolor

**İstek:** "gradient fill kısmına farklı türlerde gradient eklermisin. ve multicolor özelliğide olsun"

**Önceki durum:** `DrawingTools.Gradient` yalnızca doğrusal (linear) idi ve sadece iki renk
(foreground → background) kullanıyordu. Ordered dithering (Bayer 4×4) sayesinde palete yeni renk
eklemiyordu; bu garanti korunacak şekilde genişletildi.

**Core (`DrawingTools.cs`):**
- Yeni `GradientShape` enum'u — Photoshop'un seti: `Linear`, `Reflected`, `Radial`, `Diamond`,
  `Square`, `Angle` (konik). Square, halı bordürlerinde işe yarayan iç içe dikdörtgen rampasıdır.
- Her şekil, sürükleme vektörünün iki izdüşümünden türetiliyor: `along` (vektör üzerine) ve
  `across` (dikine), ikisi de sürükleme uzunluğu 1.0 olacak şekilde normalize. Böylece Radial
  mesafe, Diamond `|along|+|across|`, Square `max(|along|,|across|)`, Angle ise atan2 farkı.
- **Multicolor:** yeni aşırı yükleme `IReadOnlyList<byte> stops` alıyor. Duraklar sürüklemenin
  başından sonuna eşit aralıklı yerleşiyor; `t` hangi iki durak arasına düşüyorsa dithering
  yalnızca **o iki durak** arasında yapılıyor. Bu, "desenin paleti asla değişmez" kuralını
  korumanın anahtarı — indexed bir halı deseninde ara renk uydurmak kabul edilemez.
- `dither: false` seçeneği sert bantlar üretiyor (kademeli "ombre" bordür için).
- Eski iki renkli imza korundu; yeni imzaya delege ediyor, çağıran kod kırılmadı.

**ViewModel (`DesignSurfaceViewModel.Gradient.cs`, yeni):** `GradientShape`, `GradientDither`,
`GradientMulticolor`, `GradientStops` (ObservableCollection), Add/Remove/Clear komutları ve
`ActiveGradientStops`. Multicolor açık ama ikiden az durak varsa foreground/background çiftine
düşüyor — araç hiçbir koşulda sessizce hiçbir şey yapmaz.

**UI (`ToolOptionsPanel.xaml`):** Gradient'e özel bölüm — tür ComboBox'ı, Dither ve Multicolor
kutuları, durak renklerinin swatch listesi (sağ tık ile silme), "Add foreground" / "Clear"
düğmeleri ve açıklama metni. `DrawingToolCatalog` açıklaması da güncellendi.

**Testler (3 yeni, toplam 92 yeşil):**
- `Gradient_MulticolorRunsThroughEveryStopInOrder` — 3 duraklı rampa başta/ortada/sonda doğru.
- `Gradient_ShapesProduceDifferentFills` — Resize scale modlarında bir kez yaşanan "hepsi aynı
  çıktı" hatasının tekrarını engelliyor: her şekil Linear'dan farklı sonuç vermek zorunda.
- `Gradient_NeverIntroducesAnIndexOutsideTheStops` — tüm şekiller için palet garantisi.

**Doğrulama:** build 0 hata, Core testleri 92/92 yeşil, uygulama yeniden başlatıldı (PID 21412).

## Bölüm 48 — Gradient Colors penceresi (sürükle-bırak) + Color Gradient aracı

**İstek:** "multicolor penceresi yap ve o penceredeki renkleri kullansın. paletten rengi sürükle
bırak ile multicolor içerisine alınabilinsin. ayrıca gradientte ek olarak magic wand gibi tek
tıkla bir seçili olan kısma veya tüm renge gradient atılabilinsin."

### 48.1 Gradient Colors penceresi

`Windows/GradientColorsWindow.xaml(.cs)` — **modeless** (`Show()`, `ShowDialog()` değil). Bu bir
tercih değil zorunluluk: modal pencere arkadaki Color Palette panelini bloke ederdi, oysa özelliğin
tamamı oradan renk sürüklemeye dayanıyor.

İçerik: duraklardan oluşan rampanın canlı önizleme şeridi, "Use these colors (Multicolor)" kutusu,
sıralı durak listesi (her satırda renk, palet indeksi, ▲/▼ sıra değiştirme, ✕ silme) ve
Add foreground / Clear / Close düğmeleri.

**Sürükle-bırak:**
- Kaynak: `PalettePanel`. Swatch'a basıldığında indeks ve basma noktası kaydediliyor;
  `OnSwatchMouseMove` ancak sistemin sürükleme eşiği (`SystemParameters.MinimumHorizontal/
  VerticalDragDistance`) aşılınca `DragDrop.DoDragDrop` başlatıyor. Eşik olmadan normal tıklama
  (renk seçme) yutulurdu.
- Sözleşme: `PaletteDragDrop.Format = "RugCAD.PaletteIndex"`, veri olarak sadece `int` indeks.
  Hedef rengi aktif desenin kendi paletinden okuduğu için bırakılan renk her zaman o desenin
  gerçek bir palet girdisi oluyor.
- Hedef: pencerenin tamamı `AllowDrop`. `DropPosition` bırakılan noktanın hangi satırın üst/alt
  yarısına denk geldiğine bakıp `InsertGradientStopIndex` ile **araya** ekliyor; satırların dışına
  bırakınca sona ekliyor. Durak sırası rampa sırası olduğu için bu kozmetik değil gerçek bir düzenleme.

**Aktif belge senkronu:** Pencere dock'lu olmadığı için `DataContext`'i kendiliğinden takip etmez.
`MainWindow.OnActiveContentChanged` tek örneği yeniden hedefliyor — aksi halde kullanıcı sekme
değiştirdikten sonra kapalı/başka bir desenin durak listesini düzenlemeye devam ederdi.

### 48.2 Color Gradient aracı (yeni)

`DrawTool.ColorGradient`, Fill grubunda. Sürükleme yok, Magic Wand gibi tek tık:
- Aktif seçim varsa → seçim doldurulur.
- Yoksa → tıklanan pikselin renk bölgesi (`WandTolerance`/`WandContiguous` kuralları geçerli).
- **Çift tık** → o rengi desenin tamamında doldurur (Magic Wand çift tıkı ile aynı davranış).

Magic Wand'ın bölge testi `SelectWand` içinden `BuildWandMask(x, y, contiguous?)` olarak ayrıştırıldı
ve iki araç arasında paylaşılıyor — kural kopyalanmadı.

**Yön sorunu ve çözümü:** Sürükleme olmadığı için başlangıç/bitiş noktası yok. Yeni `GradientAngle`
(derece, Tool Options'ta slider + kutu) ile rampa **bölgenin kendi sınırlayıcı kutusu** üzerine
seriliyor: merkezli şekiller (Radial/Diamond/Square/Angle) merkezden köşelere, Linear köşe
izdüşümünden köşe izdüşümüne, Reflected merkezden iki yöne. Böylece rampa bölge şekli ne olursa
olsun tam olarak bölgeyi kaplıyor. `reach = |cos|·yarıGenişlik + |sin|·yarıYükseklik` hesabı,
seçilen açıda kutunun gerçek uzanımını veriyor.

Simge (`ToolIcon`): normal gradient alanı + wand kıvılcımı.

**Tool Options:** Gradient bölümü artık `Gradient|ColorGradient` için görünüyor; içine salt okunur
durak swatch şeridi, "Gradient Colors..." düğmesi ve yalnızca ColorGradient'te görünen Angle
kontrolü eklendi.

**Doğrulama:** build 0 hata, Core testleri 92/92 yeşil, uygulama yeniden başlatıldı (PID 32072).

## Bölüm 49 — Panels/Reset Layout View'a geri taşındı, Multicolor dockable panel oldu

**İstek:** "menudeki window>panel ve reset layout to default kısımlarını view> içerisine al ve
multicolor penceresi color palette gibi taşına bilir ve ui tarafında yerleşilebilir şeklinde
olmalı. view panels içerisinede multicolor ekle"

### 49.1 Menü

`Panels` alt menüsü ve `Reset Layout to Default`, Window'dan View'a taşındı (Bölüm 20'de tersi
yapılmıştı; kullanıcı kararını değiştirdi). Window menüsünde artık sadece açık sekme listesi var —
bu yüzden hiç desen açık değilken menü boş bir popup olmasın diye `NoOpenDesignsMenuItem`
("No open designs", pasif) eklendi ve `OnWindowMenuOpened` sekme sayısına göre gösterip gizliyor.
Artık işlevsiz kalan `WindowMenuTabsEnd` ayıracı kaldırıldı.

### 49.2 Multicolor artık bir panel

Bölüm 48'de yapılan `GradientColorsWindow` **silindi**, yerine
`Controls/GradientColorsPanel.xaml(.cs)` geldi ve `RightPane` içinde Color Palette'in yanına
`GradientColorsAnchorable` (başlık: "Multicolor") olarak yerleşti. Böylece diğer paneller gibi
sürüklenebilir, sekme yapılabilir, float edilebilir.

Panel olmanın getirdiği sadeleşme: pencere sürümünde `MainWindow`'un tek örneği tutması, sekme
değişiminde `DataContext`'i elle yeniden hedeflemesi ve `Closed` ile temizlemesi gerekiyordu.
Panel `DataContext="{Binding}"` ile Window'un DataContext'ini takip ettiği için bu kodun tamamı
gereksizleşti ve kaldırıldı — aktif desen senkronu artık diğer panellerle aynı mekanizma.

Sürükle-bırak mantığı (`PaletteDragDrop.Format`, satır üst/alt yarısına göre araya ekleme)
aynen korundu, sadece panele taşındı.

**Bağlantılar:** View > Panels > Multicolor (`OnShowGradientColorsPanel`), Tool Options'taki düğme
artık pencere açmak yerine paneli öne getiriyor (`ShowGradientColorsPanel`), ve `OnResetLayout`
paneli de RightPane'e geri koyuyor. Reset'te Color Palette en son aktive ediliyor ki sağ panelde
üstte kalan sekme o olsun.

**Doğrulama:** build 0 hata, uygulama yeniden başlatıldı (PID 2788).

## Bölüm 50 — Color Gradient çift tık hatası, gelişmiş ayarlar, paper format, layout kalıcılığı

Bu bölüm arka arkaya gelen beş isteği kapsıyor.

### 50.1 Çift tık hatası (gerçek hata)

**Şikâyet:** "ben griye çift tıklıyorum ancak beyaza veriyor. çünkü çift tıkladığım esnada ilk
taramayı atıp mousenin geldiği noktaya beyaz geliyor ve bu sefer beyaza komple veriyor."

Kullanıcı sebebi doğru teşhis etmiş. Çift tık `ClickCount=1` sonra `ClickCount=2` olarak geliyor;
**ilk tık zaten dolguyu uygulamış** oluyor, dolayısıyla ikinci tık `Document.GetPixel(x,y)` ile
rampanın ilk durağını (beyaz) okuyor ve tüm deseni ona göre dolduruyor.

**Çözüm (`ApplyGradientToRegion`):**
- Her bölge dolgusu, kullandığı kaynak rengi ve ürettiği komutu `_lastRegionFill` içinde saklıyor.
- İkinci tık geldiğinde, aynı noktadaysa ve o komut hâlâ undo yığınının tepesindeyse
  (`History.PeekUndo` ile doğrulanıyor) ilk dolgu `History.Undo()` ile geri alınıyor ve
  **yeniden örnekleme yapılmadan** saklanan kaynak renk kullanılıyor.
- Bunun için `CommandHistory.PeekUndo` ve `DesignSurfaceViewModel.LastPaintCommand` eklendi;
  `BuildWandMask` opsiyonel `sourceIndex` parametresi aldı. Körlemesine undo yapılmıyor — komut
  kimliği kontrol ediliyor, aksi halde araya giren başka bir işlem geri alınabilirdi.

### 50.2 Texcelle'deki gibi gelişmiş ayarlar

`GradientOptions` kaydı (Core) tüm ayarları tek yerde topluyor; `DrawingTools.Gradient` artık onu
alıyor (eski imzalar korundu).

- **Style: Normal / Weave** + **Gradient Weave** listesi. Dither eşik matrisi rastgele bir detay
  değil, **dokumanın kendisidir**: saten eşik sırası ikinci rengi saten dokumanın atlamalarıyla
  aynı ritimde yerleştirir. `GradientWeaves` gerçek dokuma yapıları üretiyor:
  `rank(x,y) = ((x - y·step) mod N)·N + y` — her step için bijeksiyon, yani her hücre ayrı eşik.
  step=1 keper (twill), N ile aralarında asal step>1 saten. Plain(2/1), Twill(8/1), Satin5(5/3),
  Satin5Reverse(5/2), Satin8(8/3), Satin13(13/5), Line, Bayer, BayerFine ve ekstramız Noise.
  Texcelle'in isim listesi kopyalanmadı; kendi İngilizce adlarımızla gerçek yapılar üretiliyor.
- **Transition (Rough ↔ Fine):** iki durak arasındaki karışım bölgesinin genişliği.
  `sharpened = 0.5 + (fraction - 0.5) / transition` — 1'de tam yumuşak, 0'da sert bantlar.
- **Point Size X / Y:** rampa piksel yerine blok başına hesaplanıyor (kaba düğüm). X ve Y ayrı,
  çünkü tarak ve atkı sayıları farklı.
- **Type'a iki yeni tür:** `Outline` (bölgenin kendi konturundan içeri doğru, BFS mesafe dönüşümü)
  ve `Shade` (kontur derinliği + yönlü rampa karışımı). Bunlar bölge gerektirdiği için ayrı bir
  Core fonksiyonu: `OutlineGradient(mask, stops, options, directionWeight, angleDegrees)`.
  Sürükleme tabanlı Gradient aracında bu türler seçilirse sürükleme yok sayılıp seçim (yoksa tüm
  desen) dolduruluyor — sessizce doğrusal rampaya düşmüyor.
- **Canlı önizleme:** Multicolor panelinin altına, ayarların tamamıyla (tür, dokuma, transition,
  point size, paper format) **aynı Core çağrısı** kullanılarak render edilen önizleme eklendi.
  Aynı kod yolu olduğu için önizleme ile gerçek sonuç birbirinden ayrışamaz.

### 50.3 Paper format'a uyum

**İstek:** "gradientler paper formata göre efektleri atsın"

`GradientOptions.AspectY = Warp / Weft`. Tüm mesafeler fiziksel uzayda ölçülüyor (dikey adım
`AspectY` kadar sayılıyor). 48/75 kalitede grid biriminde hesaplanan bir Radial gradient tezgâhtan
oval çıkardı; artık halının üzerinde gerçek daire. Outline'ın BFS mesafesi de aynı şekilde
ağırlıklı. "Follow Paper Format" kutusu ile kapatılabiliyor, varsayılan açık.

### 50.4 Radial merkezi + halı gölgeleme efekti

- **"Center on click point":** merkezli türler (Radial, Diamond, Square, Angle) normalde bölgenin
  ortasını merkez alır; bu kutu açıkken **tıklanan pikseli** merkez alıyor ve rampa uzunluğu o
  noktadan bölgenin en uzak köşesine göre hesaplanıyor, böylece yine tüm bölgeyi kaplıyor.
- **Shade türü + "Carpet shading preset":** kullanıcının gönderdiği halı görselindeki püskürtülmüş,
  bulutlu gölgeleme. Shade, konturdan gelen derinliği yönlü bir rampayla karıştırıyor
  (`Shade direction` kaydırıcısı: Contour ↔ Directional), Noise dokumasıyla da yapısız/grenli bir
  geçiş veriyor. Preset düğmesi tek tıkla Shade + Noise + Fine + %55 yön ayarlıyor.

### 50.5 Layout kalıcılığı

**İstek:** "kullanıcının yaptığı layout kaydedilsin bir sonraki oturumda son layout şeklinde açılsın"

`MainWindow.Layout.cs` (yeni). **Önemli bulgu:** bu AvalonDock 5.0.0 derlemesinde `XmlLayoutSerializer`
**yok**; onun yerine `AvalonDock.Serialization.LayoutDtoMapper` (`ToDto`/`FromDto`) ve
`XmlRoot` nitelikli DTO ağacı var. Kalıcılık bunun üzerinden yapılıyor,
`%AppData%\RugCAD\layout.xml`.

- Panellere XAML'de açık `ContentId` verildi (ToolOptions, ActivePatterns, PaperFormat,
  ColorPalette, GradientColors). DTO yalnızca kimlik taşıdığı için geri yüklemede her panele canlı
  içeriği ContentId ile elden veriliyor; eşleşmeyen varsa kapatılıyor.
- **Desen sekmeleri kasıtlı olarak geri yüklenmiyor:** taşınmış/silinmiş dosyalara ait runtime
  dokümanlar, boş hayalet sekme olarak dönmemeli.
- `RebindAnchorables`: layout değiştikten sonra XAML'den gelen `x:Name` alanları artık yetim
  nesneleri gösteriyor. View > Panels ve Reset Layout bu alanlar üzerinden çalıştığı için hepsi
  (paneller, LeftPane/RightPane, DocumentPane) yeni ağaca yeniden bağlanıyor — yoksa menü sessizce
  hiçbir şey yapmazdı.
- Reset Layout kaydedilmiş dosyayı da siliyor; yoksa sıfırlama bir sonraki açılışta geri alınırdı.
- Tüm hata yolları sessiz: eski/bozuk bir layout dosyası uygulamanın açılmasını engellememeli,
  XAML'deki varsayılan layout zaten geçerli bir yedek.

**Doğrulama:** build 0 hata, Core testleri **107/107** yeşil (yeni: dokuma matrisi bijeksiyon
testi ×9, dokumaların birbirinden farklılığı, Rough sert bant, Point Size bloklama, Radial'in
paper format'a uyumu, Outline'ın konturdan başlaması, Shade'in gerçek karışım olması).
Layout kalıcılığı elle doğrulandı: uygulama kapatıldı → `layout.xml` (909 bayt, doğru ContentId'ler)
oluştu → yeniden açıldı ve sorunsuz yüklendi (PID 6324).

## Bölüm 51 — Halı sektörü efektleri (6 yeni tür + preset listesi)

**İstek:** "5-6 tane daha halı sektöründe kullanabileceğimiz efektler yaparmısın."

**Tasarım gerekçesi (kod yorumlarına da yazıldı):** Dokunmuş bir halı basılı bir poster değil;
yüzeyinde boya partisi kayması, harmanlanmış iplik ve tezgâh ritmi vardır. Matematiksel olarak
kusursuz bir rampa "makine işi" gibi okunur. Bu efektler el dokumasındaki düzensizliği geri katıyor.

**Yeni `GradientShape` değerleri:**
- `Abrash` — el dokumasında dokuyucu yeni boya partisine geçtiğinde oluşan yatay ton bantlaması.
  Bantlar atkı boyunca, düzensiz yükseklik ve tonlarda (1-B value noise), kenarları cetvel gibi
  düz olmasın diye piksel başına küçük bir sapmayla.
- `Wave` — rampa boyunca sinüzoidal tekrar eden yumuşak dalgalar.
- `Clouds` — fraktal (çok oktavlı) value noise; elde boyanmış hav dokusunun bulutlu, eşitsiz tonu.
- `Rays` — tıklanan noktadan yayılan ışınlar (madalyon/köşe işleri).
- `Rings` — tıklanan nokta etrafında tekrarlayan eş merkezli halkalar.
- `Speckle` — melanj/kırçıl: taban rampaya piksel başına rastgele sapma.

**Destekleyen altyapı:**
- `GradientNoise` (Core): `Hash`, yumuşak `Value` ve çok oktavlı `Fractal`. **Deterministik** —
  aynı desen, ayar ve seed her zaman aynı halıyı vermeli, yoksa onaylanmış bir numune yeniden
  üretilemezdi.
- `GradientOptions`'a `Frequency` (tekrar sayısı / noise ölçeği), `Jitter` (rastgelelik oranı) ve
  `Seed` eklendi. UI'da "Repeat / scale", "Randomness" kaydırıcıları ve seed'i yeniden atan
  "New variation" düğmesi var.
- `Triangle()` yardımcı fonksiyonu: tekrarlayan efektlerde rampa çıkıp geri iniyor, böylece ardışık
  tekrarlar son duraktan ilk durağa zıplamak yerine dikişsiz birleşiyor.

**Preset listesi (`GradientPresets`):** Carpet shading, Abrash bands, Antique clouds, Melange yarn,
Sunburst rays, Medallion rings, Watered silk, Contour bands. Her preset sadece yukarıdaki ayarların
bir kombinasyonu — kara kutu değil: uygulandıktan sonra her kaydırıcı ne yaptığını gösteriyor ve
oradan düzenlenebiliyor. Tool Options'ta açıklama metniyle birlikte en üstte.

**Testler (3 yeni, toplam 110 yeşil):**
- `CarpetEffects_AreAllDistinctAndPaletteSafe` — yedi türün hepsi farklı çıktı vermeli (Resize
  scale modlarındaki "hepsi aynı" hatasının tekrarını engelliyor) ve hiçbiri durak dışı renk
  üretmemeli.
- `CarpetEffects_AreDeterministicPerSeed` — aynı seed aynı sonuç, farklı seed farklı sonuç.
- `CarpetEffects_FrequencyControlsRepeatCount` — frekans artınca bant sayısı gerçekten artmalı
  (sadece ölçeklenmemeli).

**Doğrulama:** build 0 hata, Core testleri 110/110 yeşil, uygulama çalışıyor (PID 38232).

## Stamp clone ve Gradient/Bucket All–Single (18.09.2026)

- Stamp normal modu Clone Stamp: Alt+click kaynak, basılı tutarak round brush ile kopyalama; kaynak/target ofseti Aligned ile korunur, kapalıyken her stroke aynı kaynak noktasından başlar. Brush Size, source preview ve kaynak crosshair eklendi. Alt+click daha önce seçilmiş pattern modunu kapatarak kaynak belirler; pattern modu ayrı seçeneğiyle brush gibi devam eder.
- CloneStamp Core lazily korunan stroke öncesi target piksellerini kaynak okumasında kullanır; overlapping kopya kendi yeni boyadığı pikselleri tekrar örnekleyip smear yapmaz. Her stroke tek undo; canlı bitmap pixel patch yolu kullanılır.
- Gradient / Color Gradient ayarlarına All matching color areas checkbox: kapalı Single bağlı alan, açık All aynı rengin bütün ayrı alanları. Drag gradient de aynı kapsam maskesini kullanır. Color Gradient çift tıklama All kullanır; ilk tıklamanın boyadığı rengin ikinci tıklamada yanlış kaynak olması önlenir. Selection varsa seçim sınırı korunur.
- Bucket çift tıklama Global Fill checkbox durumundan bağımsız aynı rengin tüm piksellerini doldurur. İlk tıklamanın fill history adımı rollback edilip ikinci dolgu özgün renk üzerinde tek undo adımı oluşturur. Tek tıklama checkbox tercihini kullanır. Sağ tıklama background fill aynı yolu kullanır.
- Adobe Clone Stamp kaynağı: https://helpx.adobe.com/sg/photoshop/desktop/repair-retouch/heal-clone/retouch-images-with-the-clone-stamp-tool.html
- 114 Core testi geçti; model kontrolü Clone paint/undo/redo, Gradient Single/All/doubleclick/undo, Bucket doubleclick/source/undo geçti. GUI host kullanılmadı. Standart Debug çıktısı hatasız yeniden derlendi.

## Stamp kaynak artısının kaldırılması

- Kullanıcı tercihiyle Clone Stamp kaynak crosshair çizimi kaldırıldı. Fırça çemberi, kaynak piksel önizlemesi, Alt+click kaynak seçimi ve Aligned davranışı korunur.
- Standart Debug çıktısı başarıyla güncellendi.

## Layout otomatik kaydı ve desen sekmelerini geri yüklememe

- Layout artık yalnızca Closing'de değil, dock model ağacı ve panel konum/boyut/sekme/visibility değişikliklerinden sonra UI dispatcher üzerinde birleştirilmiş otomatik kayıtla yazılır. Panel drag/resize mouse-up sonu bekleyen kayıt ayrıca flush edilir. Ayrı bir kapanış beklemeye gerek yoktur.
- PanelLayoutPersistence XML'e LayoutDocument ve LayoutDocumentFloatingWindow içeriklerini koymaz; eski layout yüklenirken de bu öğeler temizlenir. Document pane düzeni boş olarak korunur; eski kayıtta yalnızca yüzen desen varsa yeni boş document pane eklenir. Desen dosyaları otomatik açılmaz.
- Layout XML aynı dizindeki geçici dosyaya WriteThrough + Flush(true) ile tam yazılır, ardından mevcut dosyanın yerine atomik taşınır. Yazı esnasında process kapatılsa önceki tam dosya korunur; normal kapanış son bir kayıt yapıp event subscription'larını kaldırır.
- DTO kontrolleri: panel DockWidth değişimi bildirim verdi; width=444 ve ContentId roundtrip korundu; normal/yüzen desen sekmeleri persist/restore edilmedi; eski layout migration; atomik overwrite; kayıt sonrası test child process force-kill ve dosya okuması geçti. GUI host açılmadı; testler yalnızca modelleri kullandı.
- Bu güncelleme öncesi çalışan eski uygulama normal kapatıldı; kullanıcının henüz eski sürümdeki son layout'u da kaydedildi. Standart Debug çıktısı hatasız güncellendi, 114 Core testi geçti.

## Palette Park/Stop ve hover renk kısayolları

- Kullanıcı teyidi: Stop rengin üzerini boyamayı engeller; Park deseni görünümden gizlemez, selection işlemlerinde o rengi dışarıda bırakır. Stop için ileride anlatılacak ek davranış bu değişikliğe dahil edilmedi.
- Canvas focus + pointer canvas pikselleri üzerindeyken Space foreground, Ctrl+Space second/background, Shift+Space Stop toggle, Ctrl+Shift+Space Park toggle. Kısayollar Edit > Keyboard Shortcuts'a kayıtlıdır; press auto-repeat toggle yapmaz; panel/text focus koruması sürer.
- Palette renk hücresinin sağ üst köşesine Shift+click Park, sağ alt/sağ köşeye Shift+click Stop toggle. Park mavi P, Stop kırmızı kare marker ile görünür. Fore/second swatch alanının sağına Park/Stop dropdown eklendi: All, None, Invert, Except (foreground dışındaki tüm renkler), Load..., Save....
- Stop target pixel koruması canlı kalem/fırça, shape/pattern/gradient/clone boyama ve doğrudan pixel edit commit yollarına bağlandı. Undo/redo geçmişte uygulanmış pixel değişimini normal geri alabilir.
- Park etkin renkler IsPixelSelected, coverage, selection contour, copy/capture/selection motif snapshot'tan dışlanır; toggle kapatılınca aynı maskede yeniden seçilebilir. Seçim yokken Park normal boyamayı engellemez. Desen pikselleri ve renkler değişmez.
- Durumlar document StopColors/ParkColors kümelerinde; .rugcad format v2 palette flag byte'larıyla saklanır, v1 dosyaları okumaya devam eder. Resize VM yeni document'a flags taşır. Ayrı .rugcolors JSON listeleri menu Load/Save ile tip/indeks doğrulamalı yüklenir.
- Core 117 test geçti (native flags roundtrip, v1 uyumluluğu, truncated flags dahil). Model kontrolü Stop paint/live, Park selection/coverage/capture/contour/toggle, Park ordinary paint, All/None/Invert/Except, load/save, resize retention geçti. GUI host kullanılmadı; standart Debug çıktısı hatasız güncellendi.


### Selection movement: explicit preview and apply
- Selection tools drag selected pixels directly; Ctrl is no longer required for moving content. Shift/Alt selection combination gestures remain available.
- Release changes the floating preview position only. Repeated drags and arrow-key nudges retain the original source capture. Enter applies once; Escape cancels.
- Tool Options exposes Duplicate, Apply and Cancel. Copy previews preserve the source; moving previews erase only the original source visually.
- Move without a selection no longer selects the entire document automatically.
- Validation: Debug build succeeded; 117 Core tests passed; safe STA console checks verified repeated preview, cancellation, move/copy, duplicate, Active Pattern and undo/redo without opening an application window.

### Selection drag preserves source by default
- Normal selection drag and arrow nudges now create source-preserving previews and commits. The original is visible during preview and remains after Enter.
- Explicit Cut (Ctrl+X) continues to remove the source and populate the clipboard.
- Safe STA console checks passed for repeated drag, apply, undo, cancel, Cut and Paste.


### Enter finishes selection; Duplicate continues; command-picker shortcut editor
- Enter applies pending normal selections and closes the selection. Enter also deselects unmoved normal selections. Commit history records the completed state, so redo does not reopen it.
- Explicit Duplicate Selection keeps its selection for repeated positioning and stamping. A newly created selection resets this behavior.
- File > Close closes the current design tab; configurable Ctrl+W default.
- Keyboard Shortcuts clones the current main menu and toolbars, including drawing icons. Clicking selects an assignable command without executing it, even if unavailable in the active document. Search/overview, conflict checks, assignment/removal, defaults and OK/Cancel remain available.
- Fixed shortcut paths for panel commands now under View, and registered Multicolor.
- Validation: safe STA checks verified Enter, duplicate continuation/reset, undo/redo, menu and toolbar selection without running commands; 117 Core tests passed; Debug build succeeded.


## Bölüm 52 — Color Gradient All/Single seçeneği ve desenli seçim gösterimi

### 52.1 All / Single onay kutusu

**Şikâyet:** "bazen çift tıklama bütün renklere işe yaramıyor."

Haklı: çift tık, sistem çift tık süresi içinde **aynı piksele iki kez** isabet etmeyi gerektirir —
büyütülmüş bir desende kolayca kaçar. Kısayol kalsın diye kaldırılmadı, ama kalıcı ve açık bir
seçenek eklendi: `GradientFillWholeColor` (Tool Options'ta "Fill all of that color").

- Kapalı (Single): yalnızca tıklanan bağlantılı parça.
- Açık (All): desendeki o rengin tüm pikselleri.
- Çift tık, kutunun durumundan bağımsız olarak yine All yapıyor.

Canvas çağrısı: `ApplyGradientToRegion(x, y, e.ClickCount >= 2 || GradientFillWholeColor)`.

### 52.2 Seçim gösterimi: marching ants yerine siyah/beyaz desen

**İstek:** "selection bu şekilde çizgi yapmak yerine üzerinde siyah/beyaz ton bir şey versek çünkü
çizgiler çok kötü oluyor... texcellede kullanılan selection... menüde select kısmında toggle."

Bu sadece bir görsel tercih değil: halı deseninde seçilen şekil genelde ince ve parçalı oluyor,
her pikselinin etrafına çizilen kontur altındaki deseni gizleyen bir çizgi yığınına dönüşüyor
(Bölüm 46'daki 234k kenar bunun uç örneğiydi). Desen kaplaması alanı **çevrelemek yerine örtüyor**,
bu yüzden seçim ne kadar girift olursa olsun okunabilir kalıyor.

- `CanvasViewOptions.SelectionAsPattern` (uygulama geneli, Scrollbars kalıbının aynısı) +
  Select menüsünde "Show Selection as Pattern" toggle'ı.
- `DrawSelectionPatternVeil`: seçili piksellere 1 piksellik siyah/beyaz dama, alfa 150. Yarısı
  beyaz yarısı siyah olduğu için altındaki her renk üzerinde nötr bir parıltı olarak okunuyor.
  Seçim sürümü başına bir kez rasterize ediliyor, sonra kare başına tek blit — yani düzgün bir
  dikdörtgen ile çeyrek milyon dağınık piksel aynı maliyette.
- Tek yerden anahtarlama: taahhüt edilmiş seçimi gösteren tüm yollar zaten
  `DrawCachedSelectionOutline` üzerinden geçtiği için kontrol oraya kondu. Yüzen (floating) parçanın
  kendi konturu ants olarak kalıyor — o hareket eden bir içerik ve küçük.

**Doğrulama:** build 0 hata, Core testleri 117/117 yeşil, uygulama çalışıyor (PID 11928).

### 52.3 Düzeltme — desen değil, yarı saydam ton + köşe noktaları

Bölüm 52.2'deki dama deseni yanlış anlaşılmaydı; kullanıcı "çizgide olmasın bu şekilde pattern de
olmasın... seçilen alan siyah/beyaz transparent bir şekilde göstersin. seçim onaylanmadan önce
köşelerden point noktalar olsun. ve menüde siyah mı beyaz mı göstereceğini seçebilinsin." dedi.

- `SelectionAsPattern` (bool) yerine `SelectionDisplayStyle` enum'u: `MarchingAnts`, `BlackTint`,
  `WhiteTint`. Select > **Selection Display** altında üç madde; WPF'te radyo MenuItem olmadığı için
  tıklanan işaretlenip diğer ikisi `SyncSelectionDisplayMenu` ile temizleniyor.
- `DrawSelectionTint`: dama yok, seçili piksellerin üzerine **düz** siyah ya da beyaz, alfa 110'luk
  bir örtü. Altındaki desen görünmeye devam ediyor. Siyah mı beyaz mı okunur, tamamen alttaki
  desene bağlı olduğu için seçim kullanıcıda.
- `DrawSelectionHandles`: seçimin sınırlayıcı kutusunun dört köşesinde, siyah çerçeveli beyaz kare
  işaretler. Ekran pikseli cinsinden sabit boyut (7 px), böylece her zoom seviyesinde aynı
  büyüklükte kalıyor. İki işi birden yapıyorlar: ton kaplamasında kontur olmadığı için seçimin
  nerede bittiğini gösteriyorlar, ve seçimin hâlâ canlı olduğunu — henüz uygulanmadığını, hâlâ
  taşınabilir/birleştirilebilir olduğunu — belirtiyorlar.
- Önbellek anahtarı seçim sürümünün yanına ton rengini de aldı, yoksa siyahtan beyaza geçince eski
  bitmap kullanılırdı.

**Doğrulama:** build 0 hata, Core testleri 117/117 yeşil, uygulama çalışıyor (PID 7372).

## Bölüm 53 — Seçim kullanılabilirliği, sağ tık menüsü, lasso renk sınırı, kontur ayarları

### 53.1 Magic Wand seçimini taşıyamama (gerçek hata)

**Kök neden:** Taşımanın başlaması `IsPixelSelected(x, y)` ile **piksel tam isabetine** bağlıydı.
Dithering'li bir halı deseninde wand/lasso seçiminin kenarı tırtıklı ve dağınık olduğu için,
seçimi sürüklemeye çalışırken imleç çok sık seçili pikseller **arasındaki** boş piksele düşüyor;
o zaman taşıma başlamıyor, bunun yerine tool yeni bir seçim yapıp eskisini çöpe atıyordu.
Kullanıcının "taşıma kopyalamalarda sorun yaşıyorum" dediği şey buydu.

**Çözüm:** `CanGrabSelectionAt` — tam isabet varsa taşı, yoksa **seçimin sınırlayıcı kutusuna**
düş. Dikdörtgen marquee'de kutu ile seçim zaten aynı şey, dolayısıyla onun davranışı değişmiyor;
wand/lasso gibi parçalı şekiller de artık aynı rahatlıkta sürükleniyor. Birleştirme tuşları
(Shift/Alt) hâlâ yeni seçim başlatıyor, yani ekleme/çıkarma yolu kapanmadı.

### 53.2 Seçimde sağ tık menüsü

`DesignCanvas.xaml`'e `ContextMenu`. **Yalnızca gerçekten var olan işlemler** konuldu (Bölüm 15.3
ilkesi): Cut/Copy/Paste/Delete, Duplicate, Transform Selection..., Modify (Border/Smooth/Expand/
Feather/Contract), Grow, Similar, Inverse, Add to Active Patterns..., Save Selection..., Deselect.
Texcelle'in menüsündeki Mirror/Rotate/Skew/Macros gibi maddeler programda **olmadığı için**
eklenmedi.

`OnCanvasContextMenuOpening` menüyü, tıklama canlı bir seçimin üzerine gelmediyse tamamen iptal
ediyor — aksi halde sağ tıkın çizim araçlarındaki anlamı (arka plan rengiyle boyama, yolu bitirme)
kaybolurdu. Diyalog açan maddeler `MainWindow`'a `internal` köprülerle yönlendiriliyor, mantık
kopyalanmıyor.

### 53.3 Selection display kısayolu ve görünmeme hatası

- `Ctrl+Shift+H`: **sadece** Black ↔ White tint arasında geçiş yapıyor (istendiği gibi). Marching
  Ants menüden bilinçli bir seçim olarak kalıyor.
- **Hata:** `Ctrl+H` (Show Selection Edges) kapalıyken `DrawSelectionOverlay` en başta `return`
  ettiği için ton kaplaması da çizilmiyordu — seçim tamamen görünmez oluyordu. Artık ton stili
  etkinken ve canlı bir seçim varken bu erken çıkış atlanıyor: ton, kullanıcının seçimi **görmek
  için** yaptığı tercih, onu "kenarları gizle" ayarı bastıramaz.

### 53.4 Lasso/Polygon: tıklanan rengin dışına çıkmama

`LassoLimitToClickedColor` + `ApplyLassoSelection(points, mode, seed)`. Açıkken çizilen alan, ilk
tıklanan pikselin rengiyle eşleşen piksellere (Magic Wand toleransıyla) kırpılıyor. Motifin
etrafından kabaca geçmek yeterli oluyor, komşu renk seçime girmiyor. Hem Lasso hem Polygon
Selection kullanıyor.

### 53.5 Kontur ayarları (Texcelle Contour dialogu)

Eski hali tek bir sabit "bölgenin sınırını çiz" idi. Yenisi:
- **Area:** Single / All — tıklanan bağlantılı bölge, ya da desendeki o rengin tamamı. Magic
  Wand'ın contiguous/global kuralı paylaşılıyor (`BuildWandMask`), ikinci bir flood fill yazılmadı.
- **Type:** Inside / Outside — bant şeklin kendi pikselleri üzerine mi (şekli yiyerek), yoksa hemen
  dışına mı (şekli büyüterek).
- **Direction:** 3×3 ızgara, her yön için ayrı kalınlık. Asıl mesele bu: dokunmuş bir motife rölyef
  vermek için kontur sağ-altta 2, diğer yönlerde 1 olur; tek tip bir outline bunu ifade edemez.
  0 girilen yön hiç dokunulmadan kalıyor, yani kontur motifin tek bir tarafına konabiliyor.
- **Ek özelliğimiz:** "All sides" kaydırıcısı sekiz yönü birden ayarlıyor (eşit kontur en sık
  durum olduğu için).

Core: `DrawingTools.DirectionalContour(region, thickness, outside)`.

**Doğrulama:** build 0 hata, Core testleri 118/118 yeşil (yeni: kontur yön/iç-dış testi),
uygulama çalışıyor (PID 7724).

### 53.6 Kontur: "All sides" kaydırıcısı yerine ızgara ortasında ok düğmeleri

"All sides" kaydırıcısı kaldırıldı; yerine Direction ızgarasının **orta hücresine** ▲/▼ düğmeleri
kondu (Texcelle'de orta hücre zaten boş duruyor, doğru yer orası).

Davranış (`NudgeContourThickness`): **yalnızca hâlihazırda kullanımda olan (sıfırdan büyük) yönler**
adımlanıyor. Gerekçe: kullanıcı tek taraflı ya da dengesiz bir kontur kurduysa, kalınlığı artırmak
bilinçli olarak sıfırladığı kenarları geri diriltmemeli — konturun şekli korunmalı. Hepsi sıfırken
korunacak bir şekil olmadığı için sekiz yön birlikte adımlanıyor; böylece düğmeler eşit kontura
giden hızlı yol oluyor.

**Doğrulama:** build 0 hata, uygulama çalışıyor (PID 32412).

## Bölüm 54 — Area Selection aracı ve gruplanmış araç çubuğu

### 54.1 Area Selection (yeni araç)

Kullanıcının gönderdiği Texcelle ekran görüntüsündeki davranış: madalyonun içine tek tık atılıyor
ve seçim, **çevreleyen sınır rengine** çarpana kadar yayılıyor — arada kalan bütün diğer renkler
seçime dahil.

**Neden gerekli:** Bir halı madalyonu, tek bir kontur rengiyle çevrelenmiş, içinde onlarca renk
olan bir alandır. Magic Wand bunu seçemez (tek rengi takip eder), lasso ile elle çizmek saatler
sürer. Bu araçta işi konturun kendisi yapıyor: dolgu, desenin "motif burada biter" dediği yerde
duruyor.

`DrawTool.BoundedSelection` + `SelectBoundedArea(x, y, mode)`: tıklanan noktadan 4-komşu yayılım,
sınır rengiyle eşleşen (Magic Wand toleransıyla) piksellerde duruyor.

Tool Options: sınır rengi göstergesi, "Use foreground" ile sabitleme, "Follow the background
color" (varsayılan — palette sağ tık ile seçilen arka plan rengi), ve "Include the boundary
itself" (kontur piksellerinin seçime dahil olup olmayacağı). Sınırın üzerine tıklanırsa hiçbir şey
seçilmiyor ve bozuk görünmemesi için işlem sessizce iptal ediliyor.

### 54.2 Araç çubuğunda gruplama + alt bilgi şeridi

Araç çubuğu, sondaki araçların kimsenin bulamadığı araçlar haline geleceği kadar uzamıştı.
Photoshop kalıbı: yakın akraba araçlar tek bir yuvayı paylaşıyor.

Gruplar: (Rectangular/Elliptical/Single Row/Single Column Marquee), (Lasso/Polygonal Lasso),
(Magic Wand/Area Selection), (Gradient/Color Gradient). Sadece gerçekten akraba olanlar
gruplandı — başkası, bir aracı ona benzemeyen bir düğmenin arkasına saklamak olurdu.

- Büyük düğme yuvanın o anda gösterdiği aracı etkinleştiriyor; yanındaki küçük ▾ ok grup listesini
  açıyor. Listeden seçmek hem aracı etkinleştiriyor hem de yuvayı kalıcı olarak ona çeviriyor,
  böylece kullanıcının fiilen çalıştığı araç tek tık uzakta kalıyor.
- Tools > Drawing Tools menüsü **her aracı tek tek listelemeye devam ediyor**: gruplama bir yer
  tasarrufu, bir aracı menüden erişilemez yapma gerekçesi değil.
- Yuvaların basılı durumu düz bir binding olamaz (bir yuva birden çok aracı temsil ediyor), bu
  yüzden `SyncToolGroupsWithActiveTool` aktif aracı dinleyip elle güncelliyor — araç menüden veya
  klavye kısayoluyla da seçilebildiği için bu şart.
- **Alt bilgi şeridi:** pencerenin altına `ToolHintText` eklendi; araç düğmesinin veya menü
  maddesinin üzerine gelince aracın adı ve ne yaptığı yazıyor. Gruplanmış bir yuvanın ne
  tuttuğunu, tıklayıp aracı değiştirmeden öğrenmenin yolu bu.

**Doğrulama:** build 0 hata, Core testleri 118/118 yeşil, uygulama çalışıyor (PID 33344).

## Bölüm 55 — Transform menüsü, el kitabı, ikon kontrastı, Tile/Send to Bottom

### 55.1 Transform (yeni menü + araç çubuğu)

Core `Transforms.cs`: `FlipHorizontal`, `FlipVertical`, `Rotate180`, `RotateClockwise`,
`RotateCounterClockwise`. **Sadece dik açılar** — indexed bir halı deseninde araya karıştırılacak
ara renk yok, dolayısıyla serbest açılı bir döndürme ya renk uydurur ya da ızgarayı tırtıklı
bırakırdı. 90/180/270 tam sonuç verir.

`DesignSurfaceViewModel.Transform.cs`: hepsi **seçim varsa seçime, yoksa tüm desene** uygulanıyor —
programın geri kalanıyla aynı kural.
- Ayna ve 180°: ayak izi değişmediği için düz bir piksel düzenlemesi; normal boyama gibi undo'ya
  giriyor ve seçim hayatta kalıyor. Seçim içindeyken **yalnızca seçili pikseller** yazılıyor, yani
  tırtıklı bir şeklin aynası çevresine dikdörtgen taşırmıyor.
- 90° döndürme en/boy takas ediyor. Seçim yokken bu yeni bir doküman demek — **ve Warp/Weft
  yoğunlukları da takas ediliyor**, çünkü bir halı desenini yan çevirmek iki iplik yönünü gerçekten
  değiştirir; eski sayıları korumak bambaşka bir dokuma parçasını tarif ederdi. Seçim içindeyken
  desenin boyutu değişemeyeceği için blok seçimin merkezi etrafında döndürülüp dışarı taşan kısım
  düşürülüyor — kare olmayan bir parça için "bunu döndür"ün tek dürüst okuması bu.
- Crop to Selection: deseni seçimin sınırlayıcı kutusuna kırpıyor. Kutu içindeki seçili olmayan
  pikseller korunuyor; kırpma desenin **kapsamını** değiştirir, silgi değildir.

Menü ve araç çubuğu eklendi. Texcelle'in Transform menüsündeki Skew, Curve, Adjust, GlobalSmooth,
Shuffle, Macros **eklenmedi** — henüz yapılmadılar ve hiçbir şey yapmayan bir menü maddesi, hiç
olmayan maddeden kötüdür.

### 55.2 Araç ayarlarındaki yardım yazıları → Help > Handbook

Tool Options panelindeki uzun açıklama paragrafları kaldırıldı (bilgi ToolTip'lerde duruyor).
Yerine `Windows/HandbookWindow.cs` (Help > Handbook..., F1): arama kutusu + gruplara ayrılmış araç
referansı. Sayfa `DrawingToolCatalog` ve canlı kısayol listesinden **üretiliyor**, elle yazılmıyor —
böylece programın gerçekten sahip olduğu araçlardan sapması imkânsız; yeni bir araç eklemek
kitapçığa da otomatik ekliyor.

**Not (kendi hatam):** Bu temizliği yaparken yazdığım toplu-silme betiği çok satırlı bloklarda
araya giren öğeleri de yuttu ve ToolOptionsPanel.xaml'i bozdu (fazladan `</StackPanel>`, iki eksik
`</ItemsControl>`). Dosya git'ten geri alınıp bu oturumda eklenen dört bölüm (Area Selection,
Lasso, Contour, Gradient) elle ve temiz şekilde yeniden yazıldı. Geri alma sırasında
`BoolToIndexConverter` kaynak tanımı da gitmiş ve uygulama açılışta XamlParseException ile
çökmüştü; tekrar eklendi.

### 55.3 Menüdeki ikonların mavi seçimde kaybolması

İkonlar koyu mürekkeple çiziliyor, vurgulanan menü satırı ise arkasına mavi boyuyordu — ikon
görünmez oluyordu. `IconChip`: her ikon kendi beyaz çipinin üzerinde duruyor, böylece vurgulama
onu hiçbir zaman gizleyemiyor. Hem grup açılır listesinde hem Tools menüsünde kullanılıyor.

### 55.4 Kısayol düzenleyicide kaybolan araçlar (gerçek hata)

`KeyboardShortcutsWindow.CloneToolbar` araç çubuğu öğelerini gezerken `ButtonBase` olmayanları
atlıyordu. Bölüm 54'te eklediğim grup yuvaları birer `StackPanel` olduğu için **gruplanmış her araç
bu pencerenin araç çubuğu önizlemesinden kayboldu**. Düzeltme: panel çocukları düzleştiriliyor
(ToolIcon taşımayan ▾ oku eleniyor). Ayrıca `MainWindow.Shortcuts.cs`'te gruplanmış araçların tek
düğmeyi paylaşması yüzünden tooltip'i hep grubun son üyesi eziyordu; artık yalnızca yuvanın o an
gösterdiği üye yazıyor.

### 55.5 Window > Tile ve Send to Bottom

Texcelle MDI; bu program sekmeli dock kullanıyor. Dolayısıyla "tile" burada çocuk pencereleri
taşımak değil, doküman alanını **desen başına bir bölme**ye ayırmak demek — aynı sonuç (hepsi aynı
anda görünür), bu programın modelinde.
- Window > Tile > Horizontal (yan yana) / Vertical (alt alta) / Tabs (tek bölmeye geri).
  "Tabs" olmadan tile tek yönlü bir yolculuk olurdu.
- Window > Send to Bottom: aktif deseni altta 120 px'lik bir şeride park ediyor. Kapatmıyor —
  açık ve tek tık uzakta kalıyor, ki asıl mesele bu: kapatmak iş kaybı riski demek.

**Doğrulama:** build 0 hata, Core testleri 118/118 yeşil, uygulama çalışıyor (PID 29516).

## Bölüm 56 — Renk düzenleme: Color paneli + gelişmiş picker

**İstek:** Paletteki rengin kendisini değiştirebilme; küçük bir panel (Active Patterns'ın üstünde),
gelişmiş picker ekranını açan bir düğme, kısayol ve swatch üzerinden erişim.

**Önemli ayrım:** Şimdiye kadarki palet kodu "hangi girdiyle boyayacağım"ı seçiyordu. Bu bölüm
"o girdinin rengi ne"yi değiştiriyor. Bir palet girdisinin rengini değiştirmek, o indeksi kullanan
**tüm pikselleri aynı anda** yeniden renklendirir — halı deseninde bu çoğu zaman parçanın büyük
kısmı demek. Piksellerin kendisi hiç değişmediği için `PaletteColorCommand` yalnızca iki rengi
saklıyor: görsel etkisi ne kadar büyük olursa olsun undo maliyeti sabit.

- `Controls/ColorEditorPanel`: LeftPane'de Active Patterns'ın **üstünde**, "Color" başlığıyla.
  Seçili indeks, büyük önizleme, R/G/B kaydırıcıları, hex kutusu ve "Advanced picker..." düğmesi.
  Küçük tutuldu — sık durum (rengi biraz kaydır, hex yaz) burada, gerisi picker'da.
- `Windows/ColorPickerWindow`: doygunluk/parlaklık alanı + hue şeridi + RGB/hex kutuları.
  **Dialog (OK/Cancel), canlı uygulama değil:** her fare hareketinde uygulamak undo yığınını
  sürükleme adımlarıyla doldururdu; OK'te tek seferde işlenince tek bir undo adımı oluyor.
  Yazılan RGB'den hue/saturation/value yeniden türetiliyor ki alan ve şerit sayılarla uyumsuz
  kalmasın; gri bir renkte hue anlamsız olduğu için önceki hue korunuyor (aksi halde alan kırmızıya
  sıfırlanırdı).
- Erişim yolları — üçü de tek bir olaya (`AdvancedColorPickerRequested`) bağlı, böylece farklı
  davranamazlar: panel düğmesi, `Ctrl+Shift+C` (Colors > Edit Selected Color), ve palet swatch'ına
  **çift tık**.

**Sağ tık konusunda karar:** Kullanıcı "paletteki renge sağ tıklayınca" demişti, ancak sağ tık daha
önce yine kullanıcının isteğiyle "bu rengi arka plan yap" işlevine bağlanmıştı (Bölüm 39, Photoshop
Colors alanı). Çalışan bir jesti yerinden etmek yerine picker boşta olan çift tıka bağlandı; sağ
tık davranışı korundu. Kullanıcı isterse takas edilir.

**Doğrulama:** build 0 hata, uygulama çalışıyor (PID 9144).

## Bölüm 57 — Tile ızgarası, pan sınırlaması, Window menüsü sıralaması

### 57.1 Tile çok sayıda desende hiçbir şey göstermiyordu

**Kök neden:** `TileDocuments` bütün dokümanları **tek bir sıraya** koyuyordu. Onbeş desen tek
sıraya dizilince her birine birkaç piksel genişlik düşüyor ve hiçbir şey görünmüyor — kullanıcının
gördüğü tam olarak buydu.

**Çözüm:** Yatay tile artık **ızgaraya sarıyor**: satır başına desen sayısı `ceil(sqrt(n))`, yani
sayı arttıkça satır sayısı da artıyor ve her desen kullanılabilir bir alan koruyor. Texcelle de
aynı sebeple ızgara yapıyor (kullanıcının gönderdiği 5×3'lük ekran görüntüsü). Dikey tile tek
sütunda üst üste yığmaya devam ediyor — Texcelle'deki dikey görünümün karşılığı.

### 57.2 Desen canvas dışına kaçıyordu

`ClampPan` eklendi ve **`UpdateScrollBars` içine** kondu: her pan, zoom ve yeniden boyutlandırma
oradan geçtiği için kural tek bir yerde duruyor.
- Desen viewport'tan küçükse **ortalanıyor** — onu bir köşeye (ya da tamamen dışarı, eskiden olan
  buydu) sürüklemenin kimseye faydası yok.
- Büyükse pan serbest ama kenarlarda duruyor; canvas artık deseni hiç görünmeyecek şekilde boşluğa
  kaydırılamıyor.

### 57.3 Window menüsünde açık desenler artık altta

Sekme listesi menünün **en üstüne** ekleniyordu; Tile ve Send to Bottom sabit maddeler olduğu için
her desen açılıp kapandığında yerleri değişiyordu. Liste artık komutların altına, bir ayıraçtan
sonra ekleniyor.

**Doğrulama:** build 0 hata, uygulama çalışıyor (PID 24228).

## Bölüm 58 — Gelişmiş renk seçici (Lab + çark) ve Airbrush özellikleri

### 58.1 Color Picker: RGB + HSV + Lab + renk çarkı

Texcelle'in Color Picker'ındaki dört blok karşılandı ve genişletildi:
- **RGB** kutuları ve **HSV** alanı (doygunluk/parlaklık karesi + hue şeridi) zaten vardı.
- **Lab** eklendi: sRGB → doğrusal → XYZ (D65) → Lab dönüşümü ve tersi. Neden değerli:
  Lab **algısal olarak eşit aralıklı**; L'yi aynı miktar değiştirmek hangi renk olursa olsun aynı
  açıklık değişimi gibi görünür. RGB bunu vermez. Halıda eşit okunacak bir gölge rampası kurarken
  doğru uzay bu.
- **Color wheel** eklendi: çevrede hue, dışa doğru doygunluk, **o anki parlaklıkta** çizilmiş —
  sabit parlak bir halka çizmek, tıklayınca gelecek renkle uyuşmayan bir çark demek olurdu.
  Kimi kullanıcı çarka, kimi kareye uzanır; ikisi de aynı değerleri sürüyor, yani renk seçmenin
  ikinci ve ayrışan bir yolu değil.

### 58.2 Airbrush Properties

Core `Airbrush.cs` + `DesignSurfaceViewModel.Airbrush.cs`. Texcelle'in diyalogundaki gruplar:
- **Point Size X/Y** — her bir noktanın boyutu, X ve Y ayrı (tarak/atkı farkı).
- **Spray Area X/Y (%)** — noktaların saçıldığı alan, fırça boyutuna oranla. X ve Y eşit değilse
  elips: ızgara biriminde yuvarlak bir sprey tezgâhta yuvarlak değildir, ayrıca yatay bir yayılma
  bordür gölgelemesinde tam istenen şeydir.
- **Density + Uniform Density** — düzgün dağılım için yarıçapın karekökü örnekleniyor; kapatınca
  ham değer kullanılıyor ve gerçek bir airbrush'ın ortada yoğunlaşan düşüşü çıkıyor (gölgeleme için
  istenen budur).
- **Style** — Single Color / Multicolor / Scatter / Pattern. Multicolor ve Scatter **Multicolor
  panelinin durak listesini** kullanıyor (sırayla ve rastgele); Pattern her noktanın rengini
  seçili Active Pattern'dan alıyor. Airbrush'a ayrı bir renk seçicisi uydurmak yerine programın
  zaten sahip olduğu şeyler yeniden kullanıldı. Liste boşsa foreground'a düşüyor — stil seçmek
  aracı hiçbir şey boyamaz hale getirmiyor.
- **Repeat** (Pattern için) ve **Begin from Center** (her patlama içten dışa seriliyor).
- **Reset** düğmesi.

**Eklenmeyen:** Texcelle'in "Pressure Changes" grubu (Point Size / Spray Area / Density'yi kalem
basıncına bağlar). Burada basınç girdisi yok; hiçbir zaman çalışamayacak üç onay kutusu, hiç
olmamalarından kötü olurdu.

Çizim yolu: `PaintExtendedPoint` airbrush için `SprayAirbrush`'a sapıyor ve **piksel başına palet
indeksi** yazıyor (`PaintIndexedPixels`) — tek renkli bir stroke Multicolor/Scatter/Pattern
stillerinin hiçbirini ifade edemezdi.

### 58.3 Tile Vertical de ızgara

Bölüm 57'de yatay tile ızgaraya sarılmıştı; dikey hâlâ tek sütundu. Artık ikisi de ızgara, fark
ızgaranın oranında:
- Horizontal: az satır, çok sütun (15 → 3 × 5)
- Vertical: az sütun, çok satır (15 → 5 × 3)

`side = floor(sqrt(n))`; yatayda satır sayısı `side`, dikeyde sütun sayısı `side`. Bunlar
kullanıcının gönderdiği iki Texcelle ekran görüntüsündeki düzenlerin aynısı.

**Doğrulama:** build 0 hata, Core testleri 118/118 yeşil, uygulama çalışıyor (PID 5456).

## Bölüm 59 — Bucket özellikleri, tile/açılış ayrımı, Area Selection kontur kalınlığı, Handbook kuralı

### 59.0 KALICI KURAL — Handbook her zaman güncel

Kullanıcı: "her gelişmede handbook güncel kalmalı eksik yanlış bilgi olmamalı."

**Bundan sonra her özellik değişikliğinde** ilgili `DrawingToolCatalog` yardım metni **ve/veya**
`Tools/HandbookTopics.cs` girdisi aynı commit'te güncellenecek. Gerekçe kodun içine de yazıldı:
sessizce geçen ayın programını anlatan bir kılavuz, hiç kılavuz olmamasından kötüdür, çünkü
kullanıcı ona inanır.

`HandbookTopics.cs` yeni: araç olmayan her şey (paneller, menüler, düzenleme davranışları) burada
tek bir yerde duruyor ve yalnızca Handbook penceresi okuyor. Handbook artık hem araçları hem bu
konuları listeliyor; arama ikisinde birden çalışıyor. Önceden yalnızca araçlar vardı, yani kılavuz
programın yarısını anlatmıyordu.

### 59.1 Bucket ayarları (Texcelle Bucket Properties + eklemeler)

Core `BucketFill.cs`: `BucketOptions` + `Region()`. Bölge bir **maske** olarak dönüyor, liste
olarak değil — çünkü Border kuralının "komşum bölgede mi?" sorusunu ucuza sorabilmesi gerekiyor.

- **Fill:** Active color / Pattern / Spray Fill. Pattern seçili Active Pattern'dan, Spray Fill'in
  Multicolor'ı Multicolor panelinin listesinden besleniyor — bucket'a ayrı renk seçicileri
  uydurulmadı.
- **Area:** Single / All / Unprotected Colors. Eski "Global Fill" kutusu ve çift tık aynı Area'ya
  katlanıyor, rakip ikinci bir anahtar olarak durmuyor.
- **Ignore color protection:** Stop korumalı renklerin üzerine de basar.
- **Border:** Genişlik + Left/Right/Top/Bottom. Bölgenin kenarından içeri doğru geri çekiliyor;
  dokunmuş bir motifin konturunu yemesin diye.
- **Type:** Normal (4-komşu) / Diagonal (8-komşu) + **Continuous Fill** (renk sınırlarını aşar,
  yalnızca korumalı renklerde durur — motifin çok renkli alanını tek tıkla doldurur).
- **Spray Fill:** X/Y nokta boyutu, Density, Multicolor.
- **Reset** düğmesi.

### 59.2 Desen açılırken tile'a düşmemesi

`OpenDocumentTab` artık layout tiled ise önce `RestoreTabbedLayout()` çağırıyor. Gerekçe: tile,
kullanıcının **o an açık olan** desenler üzerinde istediği bir düzenleme; kalıcı bir mod değil.
Yeni deseni ızgaraya eklemek onu ekranın bir diliminde açmak demekti, oysa dosya açan biri tam
görünüm bekler.

### 59.3 Area Selection — sınır rengi canlı, kontur kalınlığı ayarlanabilir

- **Sınır rengi artık sabitlenmiyor:** "Use foreground" düğmesi ve sabitlenmiş indeks kaldırıldı;
  yerine Foreground / Background radyo seçimi geldi ve renk **canlı** okunuyor. Sabitlemek,
  kullanıcı başka bir renk seçer seçmez swatch'ın bayatlaması demekti. İki renk yuvası da
  değiştiğinde `RaiseBoundaryColorChanged` ile güncelleniyor.
- **"Include the boundary itself" → "Include boundary (px)":** Tek bir bayrak yalnızca dolgunun
  değdiği **tek katmanı** alıyordu; bu bir piksellik kontur için doğru, halı motiflerinin gerçek
  kalın konturları için işe yaramazdı. Artık `GrowIntoBoundary` her adımda bir halka olacak şekilde
  ve **yalnızca sınır rengi pikselleri üzerinden** büyüyor, böylece seçim konturu yiyor ama ötesine
  taşmıyor.

**Doğrulama:** build 0 hata, Core testleri 118/118 yeşil, uygulama çalışıyor (PID 38396).

## Bölüm 60 — Seçili araç ikonunun kaybolması, Colors alanında çift tık, Multicolor yazıları

### 60.1 Seçili araç düğmesinde ikon kayboluyordu (gerçek hata)

Tema, basılı (checked) `ToggleButton`'ı mavi boyuyor; ikonlar koyu mürekkeple çiziliyor ve **seçim
araçları zaten maviyle** çiziliyordu — yani seçili durumda ikon kendi arka planının içinde
kayboluyordu.

**Çözüm:** `ToolIcon`'a `Highlighted` bağımlılık özelliği eklendi; `true` iken mürekkep beyaza,
mavi ve altın vurgular açık tonlarına dönüyor. Her araç düğmesi (hem tekil hem gruplanmış yuva)
bunu kendi `IsChecked`'ine bağlıyor. Menülerdeki beyaz çip yaklaşımı araç çubuğuna uygulanmadı —
her ikonu kutuya almak araç çubuğunu ağırlaştırırdı; ikonun kendi kontrastını ayarlaması daha
temiz.

### 60.2 Colors alanındaki iki büyük swatch'a çift tık

Ön plan ve arka plan swatch'larına çift tıklayınca o girdinin renk seçicisi açılıyor. Böylece
"rengi düzenlemek için çift tıkla" kuralı **rengin gösterildiği her yerde** geçerli: palet
swatch'ı, Colors alanındaki iki kare, ve Color panelindeki düğme/kısayol.

### 60.3 Multicolor panelindeki açıklama yazısı kaldırıldı

Panelde yer kaplayan paragraf silindi; aynı bilgi listenin ToolTip'inde ve Handbook'un "Multicolor"
maddesinde duruyor. Handbook'un "Color" maddesi de çift tık yollarını anlatacak şekilde güncellendi
(Bölüm 59.0'daki kural gereği).

**Doğrulama:** build 0 hata, uygulama çalışıyor (PID 10004).


## Session tools, isolated selection editing and shortcut exchange

- Active tool and scalar settings for pencil, brush, eraser, stamp, airbrush, shapes, curve, bucket, gradients, selection and border persist throughout the session and follow the user between documents. Pattern painting choices and gradient stops are transferred too. Closing all documents keeps the tool toolbar and Tool Options bound to the retained session settings; new documents reuse them. Paper Format, palette and zoom stay document data.
- Tool shortcuts work from panels as well as canvas. Text editors retain normal keyboard input. Delete, clipboard operations and other document editing shortcuts still require canvas focus.
- Tool changes preserve a pending selection and its position. Painting with pencil/brush/stamp/eraser/bucket/gradient modifies an isolated selection capture, clipped to its mask/coverage. The source document is unchanged until Apply/Enter. Normal Enter ends the selection; Duplicate selections remain available for repeated placement.
- Mirrors, quarter turns, 180-degree rotation and selection scale/rotation change only the capture, including its mask and coverage. Buffer undo/redo restores capture and position; cancellation discards pending edits. Explicit Cut remains a source-removing operation.
- Round brush/eraser/stamp size follows Warp/Weft density independently of the Paper Format display toggle. Stamp outline and resize preview use the same proportions.
- Shortcut editor Import/Export uses JSON. Import validates gestures and rejects conflicts before changing staged settings; OK saves, Cancel discards imported changes.
- HandbookTopics and DrawingToolCatalog updated with these behaviours as required by section 59.0.
- Validation: safe STA console checks for isolated pencil/brush/bucket/gradient drawing, tool-change position, mirrors/rotation/scale, undo/redo including transform position, commit/cancel/Cut, all shared scalar tool settings across different densities, physical brush toggle independence and shortcut roundtrip/conflict rejection. Core: 118 tests passed. Existing Debug build: zero errors/warnings.


## Numpad nine-point tool actions

- User confirmed keyboard arrangement: 7/8/9 top, 4/5/6 middle, 1/2/3 bottom; 5 centre. Full design coordinates are inclusive and independent of zoom/display aspect.
- Line, rectangle, ellipse, border, gradient and rectangular/elliptical marquee use two anchors for start/end. Mouse click may finish a keyboard start. Escape cancels pending keyboard geometry; Enter finishes at the current preview.
- Curve/polyline/polygon accept anchors as points; freehand selection and lasso eraser can collect numeric vertices and close with Enter. Point paint/fill tools act at the indicated design pixel.
- Move aligns the entire selected footprint to the indicated edge/corner/centre. Shift+Numpad aligns from other tools. Moving remains a pending preview until Apply/Enter.
- Canvas > Nine points commands appear in the shortcut editor and support reassignment/import/export. Text fields and panel focus do not dispatch numeric canvas edits; top-row numbers are unaffected. Repeated numeric key-down is suppressed. Num Lock should be enabled for default gestures.
- Handbook updated. Core verification: 129 tests passed, including all nine points, even/single-pixel sizes and whole-selection alignment. Safe STA console verified line endpoints, cancellation, marquees, alignment, polyline/polygon/lasso/curve and pencil marks without showing an application window.


## Numpad immediate preview and existing-selection alignment fixes
- Numeric actions now update hover coordinates and draw an immediately visible anchor marker. First-key geometry previews towards the current mouse position instead of remaining a single pixel until mouse movement. Markers clear on mouse movement, Escape, tool or document change.
- Existing selections are aligned by Numpad in selection tools (including marquee, oval and lasso), without requiring Move. Pending moves can be snapped directly. With no selection, numeric start/end and path marking remain available. An in-progress new keyboard marquee still accepts its second endpoint.
- Handbook updated. Safe STA checks rendered the numeric marker without mouse movement, tested moved selection alignment in marquee/oval/lasso, repeated alignment, and new-selection start/end. Existing Debug build succeeded.


## Numpad fine movement and interactive selection repeat

- Num Lock off uses physical numpad Home/End/arrows/PageUp/PageDown/Clear for selection nudges. An HWND hook checks the extended-key flag so dedicated navigation keys are unaffected. Hooks follow canvas load/unload, including floating document windows; canvas keyboard focus remains required.
- Num Lock on + Shift also nudges. Default step is 1 pixel; Ctrl during either nudge mode raises it to 5 pixels. Diagonals move both axes; centre 5 does nothing. Holding a direction allows repeated nudges.
- User clarified N should activate points rather than ask for numeric dimensions. Select > Repeat Selection (N) now activates eight resize handles. Drag edges/corners to extend or shorten the repeating area; source motif size stays unchanged. N toggles handle visibility.
- Dragging within the repeated selection, or numpad nudging, shifts its pattern phase with wraparound while the area stays fixed. All changes remain in the isolated buffer until Enter/Apply; Escape/Undo of a pending repeat returns to the original selection. Repeat commits are undoable and retain the source.
- Official Texcelle page confirms motif/repeat capabilities but does not document N-key behaviour; implementation follows the user's description: https://www.nedgraphics.com/product/texcelle-design-software/
- Handbook updated. Safe STA checks passed for one/five-pixel diagonal nudges, physical key distinction, all eight handles, repeat grow/shrink, phase/wrap, cancel, commit and undo. Core tests: 129 passed. Existing Debug build succeeded.


## Selection completion, shortcut presets and menu icons

- Clicking outside an existing selection now executes the same apply/finish path as Enter. The click is consumed, so the newly chosen tool does not also paint underneath it. Duplicate's established repeated-placement behavior remains: applying it leaves its repeatable selection active.
- Delete is translated to Escape while any selection exists: a floating/repeat preview returns to its source; an idle baked selection is deselected. It no longer erases selection pixels. Curve point deletion remains available when there is no selection.
- Keyboard Shortcuts adds staged Photoshop defaults and Texcelle defaults buttons. As with Import/default reset, the preset is persisted only with OK; Cancel leaves the active configuration untouched. Commands without a verified RugCAD equivalent retain RugCAD defaults, and lower-priority collisions are cleared.
- Photoshop profile derives from Adobe's official Windows shortcut PDF: V/M/L/W/B/S/E/I/G/T/H/Z tool families, Ctrl+D/Shift+D/Shift+I, Ctrl+J/T/H/Q and modern Ctrl+Shift+Z redo. Shared tool-family letters use Shift variants because RugCAD exposes each grouped tool independently.
- Texcelle profile uses the installed reference at C:\Program Files (x86)\NedGraphics\Texcelle 2009\tips.txt for A pencil, K rectangular selection, C ellipse, L line, D eyedropper, F fill, Ctrl+N, F5 repeat, numpad zoom/movement and palette gestures. MEB's Texcelle teaching module verifies Ctrl+Z/Y/X/C/V, Ctrl+D duplicate, Ctrl+E select none, Ctrl+A and Ctrl+M make pattern. Unsupported Q color menu, O catalogue and printing were not assigned to unrelated RugCAD actions.
- MenuCommandIcon supplies clear vector font glyphs for commands that lacked icons (file, edit, selection, transform, view, panels, colors, settings and help). Existing drawing-tool vector icons remain. Icons are assigned recursively to current and dynamically rebuilt menus, and cloned into the shortcut editor's menu copy.
- Handbook updated. Safe STA checks passed for outside-click apply/undo, Delete-as-Escape for floating and baked selections, both preset maps, collision-free staging and recursive menu icon creation. Core: 129 tests passed. Debug build succeeded with no errors/warnings.

Research references: Adobe official shortcuts: https://helpx.adobe.com/photoshop/desktop/get-started/settings-and-preferences/view-keyboard-shortcuts.html ; MEB Texcelle module: https://megep.meb.gov.tr/mte_program_modul/moduller_pdf/Bilgisayarda%20Hal%C4%B1%20Deseni%20%C3%87izimi-2.pdf

## Kısayol çakışmasında atamayı devralma onayı

- Keyboard Shortcuts penceresinde bir komuta zaten başka bir komutta kullanılan kısayol atanınca artık işlem sessizce reddedilmez. Diyalog, mevcut komutun ve hedef komutun adını göstererek eski atamanın kaldırılıp kaldırılmayacağını sorar.
- **Yes** seçilirse eski komutun kısayolu boşaltılır, seçili komut yeni kısayolu alır ve değişiklik önceki tüm kısayol düzenlemeleri gibi ancak **OK** ile kalıcı olur. **No** seçilirse hiçbir atama değişmez.
- Handbook'taki Keyboard shortcuts bölümü bu davranışla güncellendi.

## Photoshop tarzı geçici kaydırma (Space + sol sürükleme)

- Aktif araç ne olursa olsun, **Space** basılıyken sol fare tuşuyla sürüklemek artık geçici Pan davranışını başlatır. Araç değişmez; kalem darbesi, şekil veya seçim başlatılmaz. Sol tuş bırakılınca önceki aracın imleci ve normal davranışı geri gelir.
- Orta tuşla sürükleyerek kaydırma ve Pan aracı korunur. `Ctrl+Space`, `Shift+Space` ve `Ctrl+Shift+Space` ise mevcut renk örnekleme / Stop / Park kısayollarını korumak için geçici kaydırmayı başlatmaz.
- Handbook'a Navigation > Temporary Pan girdisi eklendi; Pan araç açıklaması da güncellendi.

## Fare odaklı kısayol zoom'u ve seçim sağ-tık dönüşümleri

- `View > Zoom In` ve `View > Zoom Out` için Keyboard Shortcuts üzerinden atanan tuşlar artık ViewModel'in yalnızca oranı değiştiren eski yolunu kullanmaz. Aktif tuvaldeki fare konumu anchor alınır; imlecin altındaki tasarım pikseli zoom öncesi ve sonrası aynı yerde kalır.
- Seçim sağ-tık menüsüne **Crop to Selection**, **Scale** ve **Rotate** eklendi. Scale, sekiz kenar/köşe tutamacı gösterir; bir tutamacı sürükleyip bırakmak seçimin izole düzenleme buffer'ını yeni çerçeveye nearest-neighbour ile ölçekler. Rotate, 90° saat yönü, 90° ters yön ve 180° işlemlerini sunar.
- Crop ve Scale, artık üstteki **Transform** menüsünde ve Transform araç çubuğunda yer almaz. Transform menüsü yalnızca tüm desen veya etkin seçim üzerinde anlamlı olan Mirror/Rotate işlemlerini içerir.
- Handbook'taki Transform, right-click selection ve Zoom açıklamaları güncellendi.

## Tekrarlı seçimi taşıma, uygulama ve iptal

- Repeat Selection etkin iken normal fare sürüklemesi, ok tuşları ve Num Lock kapalı / Shift+Numpad ile yapılan nudge artık tekrar alanını bütün olarak taşır. Motifin desen içindeki fazı sabit kalır.
- Aynı hareketler **Alt** basılıyken eski davranışı korur: tekrar alanı yerinde kalır, motif fazı hücre boyutunda sararak kayar. Ctrl, nudge adımını 5 piksele çıkarır.
- **Deselect** (menü, sağ tık veya Ctrl+D) bekleyen floating/repeat seçimi desene uygular ve seçimi kapatır. **Escape** veya **Delete** değişiklikleri iptal eder, başlangıç piksellerini korur ve seçimi tamamen kaldırır; piksel silme yapmaz.
- Handbook'taki Selection repeat ve Selections yönergeleri güncellendi.

## Seçim dışı çizim aracı tıklaması

- Aktif bir seçim varken Pencil, Brush, Fill, şekil ve diğer çizim araçlarıyla seçimin dışına tıklamak artık seçimi uygulamaz veya kaldırmaz. Seçim etkin kalır; aracın mevcut seçim-kırpma kuralı aynen devam eder.
- Seçim dışına tıklayarak uygulama/bitirme yalnızca Move dahil seçim araçlarında yapılır. Enter ve Deselect ile uygulama, Escape/Delete ile iptal davranışları değişmedi.
- Handbook'taki Selections yönergesi güncellendi.

## Taşıma başlarken otomatik Move aracı

- Etkin bir seçim, herhangi bir seçim aracıyla içeriden sürüklenerek taşınmaya başlandığında araç otomatik olarak **Move** olur. Araç çubuğu, menü ve imleç bu geçişi anında yansıtır.
- Yeni marquee/selection başlatma ve çizim araçlarının davranışı değişmez.
- Handbook'taki Selections yönergesi güncellendi.

## Space örnekleme ve Pan

- Plain `Space` foreground renk örnekleme kısayolu key-down anında çalışır. Space basılıyken sol-sürükle Pan başlatılırsa renk örneklemesi korunur; key-up beklenmez ve Pan önceki örneklemeyi geri almaz.
- Foreground ve second/background palette indeksleri MDI oturumunda ortaktır. Başka bir pencere üzerinde örnekleme yapılsa da indeks bütün açık tasarımlara uygulanır; renk bilgisi her zaman seçili/aktif tasarımın kendi paletinden gösterilir. Ctrl/Shift'li Space kısayollarının mevcut davranışı değişmedi.
- Handbook'taki Temporary Pan yönergesi güncellendi.

## Home sayfası, Recent ve günlük ipucu

- Uygulama ilk açıldığında veya açık tasarım kalmadığında Photoshop esintili **Home** ekranı bütün dock çalışma alanını kaplar; yan paneller görünmez. Sol tarafta New design ve Open, ortada Recent kartları, sağda Tip of the Day yer alır. Tasarım açılınca Home gizlenir ve kullanıcının panel düzeni aynen geri gelir.
- Son açılan/kaydedilen en fazla 12 mevcut yerel dosya `%AppData%\\RugCAD\\recent-designs.json` içinde güvenli olarak tutulur. Kartlar PNG/BMP/JPEG/GIF/WebP için küçük görsel önizleme, `.rugcad` için desenin palet/piksel önizlemesini gösterir; okunamayan dosyalarda güvenli format kartı kalır. Eksik dosyalar görünmez; bir karta tıklamak aynı normal Open akışını kullanır.
- Tip of the Day her Home oluşturulmasında farklı bir yardım konusu seçer. Sağdaki Open Handbook düğmesi tam Handbook'u açar.
- Home, tasarım sekmesi değildir: Window menüsünün açık tasarım listesine dahil olmaz; tasarım açılınca gizlenir, son tasarım kapanınca geri gelir. Handbook'a Navigation > Home page maddesi eklendi.

## Ayarlar: Farklı Kaydet varsayılanı

- Ana menüye **Settings...** eklendi. Settings > Save As alanı RugCAD, PNG, BMP, JPEG, GIF ve WebP arasından Save As açıldığında seçili gelecek dosya türünü belirler.
- Tercih `%AppData%\\RugCAD\\save-as-preferences.json` içinde korunur; ilk değer `.rugcad`dır. Böylece Save As her seferinde zorla RugCAD yerine kullanıcının hatırlanan türünü seçer.
- Kullanıcı Save As sırasında ayardakinden farklı desteklenen bir tür seçip kaydı başarıyla tamamlarsa, uygulama bunu gelecekteki varsayılan olarak hatırlamak isteyip istemediğini Yes/No ile sorar. No, eski tercihi korur.
- Handbook'a Menus > Settings > Save As maddesi eklendi.

## Üst sıra sayı tuşlarıyla palet rengi seçimi

- Canvas odaktayken normal üst sıra `0`–`9` tuşları foreground palet indeksini seçer; numpad mevcut anchor/nudge davranışını korur. Tek bir rakam anında o indeksi seçer.
- Rakamlar kısa aralıkla birleştirilir: `1`, ardından `3` → palet indeksi `13`. Fare tıklaması/tekerlek veya sayı olmayan herhangi bir klavye eylemi biriken rakamları siler. Oluşan sayı 255'i aşarsa son rakam yeni başlangıç olur; `1` → `3` → `1` → `2` sonunda indeks `2` seçilir.
- Paleti o indekse sahip olmayan tasarımlarda seçim değiştirilmez. TextBox, ComboBox ve parola alanlarında sayılar normal metin girişi olmaya devam eder.
- Handbook'a Navigation > Palette number keys maddesi eklendi.

## Foreground / Second renklerinde palet durumu

- Color Palette içindeki tüm küçük swatch'larda palet numarası zaten görünür. Büyük foreground ve second/background swatch'larına da sırasıyla `F <indeks>` ve `S <indeks>` etiketi eklendi.
- Bu iki swatch seçili renk Park ise mavi `P`, Stop ise kırmızı kareyi gösterir. Renk seçimi, Stop/Park toggling ve toplu durum yükleme işlemlerinde işaretler anında güncellenir.
- Handbook'taki Panels > Color Palette açıklaması güncellendi.
# Son güncelleme â€” Color menüsü

- Ana menüye **Color** eklendi: Advanced Color Picker, Color Statistics, Sort Colors ve Delete Unused Colors.
- İstatistik penceresi mutlak/adet veya yüzde görünümü; kullanılmayan renkleri gösterme, renk 0'ı dışlama ve kullanıma göre sıralama seçeneklerini sağlar.
- Renk sıralama kullanım istatistiğine, parlaklığa, özgün/ters sıraya veya elle yukarı/aşağı taşımaya göre yapılabilir. Renk 0 kilitlenebilir.
- Sıralama, piksellerin paletteki indekslerini ve Park/Stop durumlarını birlikte eşler; görünüm korunur ve işlem Undo/Redo ile geri alınabilir.

## Color Range ve Home güncellemesi

- Home açıkken üst menü, araç çubukları ve durum satırı da gizlenir; yalnızca tam ekran başlangıç sayfası görünür.
- Color Statistics artık bağımsız pencere yerine yerleşimi kullanıcı tarafından değiştirilebilen dock panelidir.
- Delete Unused Colors palet girişi silmez; kullanılmayan her rengi siyaha dönüştürür.
- Select > Color Range Photoshop benzeri örnekleme penceresine taşındı: önizleme üzerinde tıklayarak renk örnekleme, Shift ile ekleme, Alt ile çıkarma, fuzziness, ters çevirme ve canlı siyah/beyaz seçim önizlemesi vardır.

## Seçim onay ve Move akışı

- Marquee, lasso, wand, alan seçimi ve Selection Brush seçimleri Enter'a kadar düzenleme aşamasında kalır.
- İlk Enter seçimi onaylar, geçici Move aracını etkinleştirir ve sekiz yeniden boyutlandırma noktasını açar. İkinci Enter seçimi uygular ve önceki araca döner.
- Move boş alanda yeni seçim oluşturmaz; onaylı seçim yoksa önceki araca geri döner. Escape/Delete seçimi iptal edip Move'dan çıkar.

## Onaylanmamış seçimde yeniden boyutlandırma noktaları

- Marquee, eliptik ve lasso seçimi çizilir çizilmez sekiz turuncu tutamak görünür. Bu aşamada bir tutamağı sürüklemek yalnızca **seçim alanını** yeniden boyutlandırır; tasarımın pikselleri değişmez. Maske (elips/lasso şekli dahil) eski sınırlarından yeni dikdörtgene en yakın komşu ile yeniden örneklenir.
- Enter seçimi onaylar: tutamaklar maviye döner, geçici Move aracı devreye girer ve aynı tutamaklar artık seçili pikselleri ölçekler (mevcut `TransformSelection` davranışı). İkinci Enter uygular.
- Yeni bir marquee sürüklemeye başlandığında ya da seçim kalmadığında tutamaklar gizlenir. Alan yeniden boyutlandırma tek bir Undo adımıdır. Tamamen tasarım dışına sürüklenen çerçevede seçim kaybedilmez, eski maske korunur.
- Kod: `ViewModels/DesignSurfaceViewModel.SelectionRegion.cs` (yeni, `ResizeSelectionRegion`), `Controls/DesignCanvas.SelectionScale.cs` (`_scaleRegionMode`, `ShowSelectionScaleHandles(regionOnly)`), `Controls/DesignCanvas.xaml.cs` (EndStroke'ta tutamakları gösterme, yeni marquee'de gizleme), `Controls/DesignCanvas.SelectionCache.cs` (seçim bitince gizleme).
- Handbook: Editing > Selections ve Rectangular Marquee araç metni güncellendi.

## Curve ve Polyline araç ayarları

- **Curve** araç ayarları paneli artık eksiksiz: üç eğri türü (Spline through points — varsayılan, Spline, Bézier) radyo düğmeleriyle seçilir. Türler zaten `CurveRasterizer` içinde vardı, ancak panelde hiçbir ayar görünmüyordu.
- Pen Size X / Y (Proportional ile Warp/Weft oranına bağlı) ve Pixel Cord seçenekleri eklendi. Pixel Cord diyagonal adımları köprüler, böylece yol yalnızca köşeden bağlanmaz.
- **Roundness (%)**: iki spline türü için eğrinin noktalar arası düz çizgiden ne kadar şiştiğini belirler (0 düz, 25 standart, üstü daha yuvarlak). Bézier'de yerine **Handle Tension (%)** çıkar: kontrol kollarının eğriyi ne kadar çektiği (100 = gerçek Bézier). Önceden Bézier'de bu değer koda 1 olarak sabitlenmişti.
- Panelde Edit / Apply / Cancel düğmeleri mevcut `CurveEditCommand`, `CurveApplyCommand`, `CurveCancelCommand` komutlarına bağlandı.
- **Draw Polyline** de aynı Pen Size X/Y + Proportional + Pixel Cord ayarlarını aldı (`PolylinePenSizeX/Y`, `PolylineSizeLinked`, `PolylinePixelCord`). Eskiden tek bir `PathPenSize` kaydırıcısı vardı; `PathPenSize` aktif aracın X kalemine takma ad olarak duruyor.
- Kod: `ViewModels/DesignSurfaceViewModel.DrawingTools.cs`, `Controls/ToolOptionsPanel.xaml`, `Controls/DesignCanvas.DrawingTools.cs` (`PathPixels`), `Controls/DesignCanvas.xaml.cs` (önizleme yenileme).

## Active Pattern bırakıldıktan sonra Move'da kalma (gerçek hata)

- Active Patterns panelinden sürükle-bırak ile konan motif `_floating` seçimi oluşturup Move aracına geçiyordu, ama `SelectionConfirmedForMove` false kalıyordu. Bu yüzden motifi ikinci kez taşımak için tıklandığında Move "onaylı seçim yok" sayıp önceki araca dönüyordu.
- `PlacePatternSelection` artık seçimi onaylanmış işaretliyor: bırakılan motif Enter beklemeden taşınabilir; Enter uygular, Escape iptal eder.
- Handbook'taki Panels > Active Patterns açıklaması güncellendi.

## Yön tuşları: her zaman pan, Shift ile seçim içinde raport kaydırma

- Yön tuşları artık **her durumda** görünümü kaydırır (pan). Seçim varken bile. Önceden aktif seçim varsa yön tuşları seçimi taşıyordu ve pan hiç çalışmıyordu.
- Enter ile **onaylanmış** bir seçim varken **Shift + yön tuşu**, seçimin *içindeki* deseni kendi sınırlayıcı kutusu içinde raport gibi sarmalı olarak kaydırır: bir kenardan çıkan karşı kenardan girer, seçim yerinde kalır, seçim dışındaki hiçbir piksel değişmez. **Ctrl+Shift+yön** 10 piksellik adım. Her kaydırma tek bir Undo adımı. Onaylanmamış seçimde Shift+yön hiçbir şey yapmaz; seçim henüz belirleniyordur.
- Seçimi klavyeyle taşımak numpad nudge'ında (Num Lock kapalı ya da Shift+Numpad) duruyor, kaybolmadı.
- Kod: yeni `ViewModels/DesignSurfaceViewModel.SelectionScroll.cs` (`ScrollSelectionContents`), `Controls/DesignCanvas.DrawingTools.cs`.

## Onaylanmamış seçimi içeriden sürükleyerek taşıma

- Enter ile onaylanmamış bir seçimin içinden sol tuşla sürüklemek artık **seçim alanını** taşır (`MoveSelectionBoundary`); tasarım pikselleri yerinde kalır. Önceden bu sürükleme yeni bir marquee başlatıyordu.
- Enter'dan sonra aynı sürükleme eskisi gibi seçili pikselleri taşır. Handbook ve Rectangular Marquee metni güncellendi.

## Curve: Delete iptal, Backspace geri alma

- Curve aracında **Delete** artık bekleyen eğrinin tamamını iptal eder (Escape ile aynı). Önceden seçili noktayı siliyordu ve seçim varsa Delete zaten Escape'e çevrildiği için davranış belirsizdi.
- **Backspace** tek adım geri alır: düzenleme modundaysa seçili noktayı kaldırır, değilse en son eklenen noktayı. Önceden düzenleme modunda Backspace tüm eğriyi siliyordu.
- Araç yardımı ve Handbook > Editing > Paths and curves güncellendi.

## Color Statistics otomatik güncelleme

- Panel artık kendini güncelliyor, **Update düğmesi kaldırıldı**. `DocumentChanged` (biten her kullanıcı eylemi: tamamlanan fırça darbesi, dolgu, undo, palet değişimi) anında yeniliyor.
- Çizim sürerken gelen `PixelsChanged` olayları 400 ms'lik bir `DispatcherTimer` ile birleştiriliyor; renk sayımı tüm pikselleri tarayan bir işlem olduğu için piksel başına yeniden sayım yapılmıyor.
- Panel DataContext değiştiğinde eski tasarımın olaylarından çıkıyor, Unloaded'da da aboneliği bırakıyor.
- Not: `Windows/ColorStatisticsWindow.xaml(.cs)` artık hiçbir yerden çağrılmıyor (panel onun yerini aldı) ve hâlâ kendi Update düğmesini taşıyor. Silinmesi ayrı bir karar olarak bekliyor.

- Panelin seçenek satırı tek sıralı `StackPanel` yerine iki sütunlu `Grid` oldu: Absolute/Percentage üstte, All colors/Sort altta. Panel dar dock edildiğinde veya pencere küçültüldüğünde kutular sağ kenardan kırpılıp kaybolmuyor.

## Color Statistics: seçim kapsamı ve renk 0

- Aktif bir seçim oluştuğunda **Selection** kutusu otomatik işaretlenir ve istatistik, yüzen seçim dahil ekranda görünen seçili piksellerden hesaplanır. Seçim kaldırıldığında kutu otomatik kapanır ve tüm desene geri dönülür.
- Varsayılan olarak kapalı olan **Exclude color 0** seçeneği eklendi. Açıldığında renk 0 hem tablodan hem de yüzde paydasından ve "toplam kullanılan renk" bilgisinden çıkarılır.

## Sort Colors düzeni ve palet etiket boyutları

- Sort Colors penceresindeki renk kartları artık `WrapPanel Orientation="Vertical"` ile **yukarıdan aşağı, sonra sağa doğru** diziliyor (Active Patterns listesindeki okuma yönü). ListBox'ın dikey kaydırması kapatıldı, yatay kaydırma açıldı.
- Kartlar 54x42'den 38x28'e küçültüldü, indeks yazısı 10 punto; aynı pencerede çok daha fazla renk görünüyor.
- Color Palette'teki büyük foreground ve second swatch'larının `F <indeks>` / `S <indeks>` etiketleri 8 puntodan 11 puntoya ve SemiBold'a çıkarıldı; küçük ekranlarda okunmuyordu.

## Klavye kısayol düzenleyicide seçilemeyen menü komutları (gerçek hata)

- `KeyboardShortcutsWindow` menüyü klonlarken, yaprak öğenin "Menü > Öğe" adını kayıtlı kısayol adlarıyla eşleştiriyor; eşleşme yoksa öğeyi `IsEnabled=false` yapıyor. Son eklenen menü komutlarının hiçbiri `MainWindow.Shortcuts.cs` içinde kayıtlı değildi, bu yüzden pencerede gri ve tıklanamaz görünüyorlardı.
- Eklenen kayıtlar: Select > Selection Display > Marching Ants / Black Tint / White Tint; Color > Advanced Color Picker / Color Statistics / Sort Colors / Delete Unused Colors; Transform > Mirror > Horizontal-Vertical ve Rotate > 90° CW / 90° CCW / 180°; View > Panels > Color ve Color Statistics; Settings...; Window > Tile > Horizontal / Vertical / Tabs; Window > Send to Bottom.
- İki farklı menüde görünen komutlar için `CommandAliases` sözlüğü eklendi: "Select > Duplicate Selection" → `edit.duplicate` (eskiden tek tek yazılıyordu), "Help > Keyboard Shortcuts..." → `edit.shortcuts`. Böylece aynı komut hangi menüden seçilirse seçilsin aynı kısayol satırına gidiyor.
- Artık ana menüdeki her yaprak öğe kısayol düzenleyicide seçilebiliyor; tek istisnalar kasıtlı olanlar: Window > No open designs (yer tutucu) ve Tools > Drawing Tools (çalışma anında doldurulan alt menü).
- Kural: menüye yeni bir komut eklenince aynı commit'te `MainWindow.Shortcuts.cs` içine de kaydı girilmeli, yoksa kısayol düzenleyicide seçilemez.

## View: Normal Size, Fit to Window, Center Design, Restore Zoom

- Eski **Reset Zoom (100%)** yalnızca yakınlaştırmayı 1.0 yapıyordu; kenara kaydırılmış bir desen 100%'de de kenarda kalıyordu, bu yüzden pratikte işe yaramıyordu. Artık dört komut var ve **hepsi konumu da düzeltiyor**:
  - **Normal Size (100%)** — 1:1 yakınlaştırma + ortalama. Eski `view.resetZoom` kimliğini koruyor, mevcut kullanıcı atamaları bozulmuyor.
  - **Fit to Window** — deseni pencereye sığdıracak yakınlaştırma + ortalama (yeni tasarım açılışındaki `FitAndCenterView` ile aynı matematik).
  - **Center Design** — yakınlaştırmaya dokunmadan ortalar.
  - **Restore Zoom** — bu komutlardan önceki yakınlaştırma *ve* kaydırmaya döner; tekrar çağrılınca geri gelir, yani iki görünüm arasında geçiş yapar. Kayıtlı görünüm yokken devre dışıdır.
- Kod: yeni `Controls/DesignCanvas.ViewCommands.cs`, `MainWindow.xaml` (View menüsü), `MainWindow.Layout.cs` (Click yönlendiricileri), `MainWindow.Shortcuts.cs` (dördü de kısayol atanabilir).

## Keyboard Shortcuts ve Settings, Tools menüsüne taşındı

- **Tools** menüsü artık Drawing Tools + ayraç + **Keyboard Shortcuts...** + **Settings...** içeriyor.
- Edit menüsündeki ve Help menüsündeki Keyboard Shortcuts girdileri ile üst seviyedeki Settings... kaldırıldı; komut artık tek bir yerde.
- Kısayol adları menüyü izliyor (`Tools > Keyboard Shortcuts...`, `Tools > Settings...`) ama kimlikleri (`edit.shortcuts`, `app.settings`) korundu; kaydedilmiş kullanıcı atamaları aynen çalışıyor.
- Help menüsündeki kopya gittiği için `KeyboardShortcutsWindow.CommandAliases` içindeki ilgili takma ad kaldırıldı; geriye yalnızca "Select > Duplicate Selection" kaldı.
- Handbook: yeni Navigation > Framing the design konusu; Editing > Keyboard shortcuts ve Menus > Tools > Settings > Save As güncellendi.

## Design > Properties ve Repeat (rapor) görünümü

Texcelle'in Design > Properties (Design Size, Paper Size, Repeat parametreleri) ve Graphic Repeats (Orientation + Drop) maddeleri ile F5 kısayolu esas alındı.

- **Design > Properties...** penceresi: ad, dosya yolu, tür, piksel boyutu, Paper Format (warp x weft), bunlardan çıkan inç cinsinden paper size, palet girişi sayısı, kullanımdaki renk sayısı ve Park/Stop sayıları. Altında repeat ayarları:
  - **Straight repeat** (kaydırmasız), **Vertical drop** (her sütun aşağı kayar), **Horizontal drop** (her satır yana kayar, tuğla düzeni), **Custom** (piksel cinsinden Drop X / Drop Y).
  - Drop kesiri 1/2, 1/3, 1/4, 1/6, 1/8. Yalnız ilgili moda ait alanlar etkin kalır; altta özet satırı efektif piksel değerini yazar.
  - Değişiklikler anında uygulanır ve **hiçbir pikseli değiştirmez**; sadece önizlemenin nasıl çizildiğini belirler.
- **Repeat görünümü**: View > Repeat View, araç çubuğundaki **Repeat** düğmesi ve **F5** aynı anahtarı çevirir. Desen, seçilen drop ile tuval boyunca döşenir; kopyalar aynı baked bitmap'ten çizildiği için her düzenlemeyi canlı izler. Gerçek desen ortadaki kopyadır, mavi çerçeveyle işaretlidir ve bütün araçlar yalnızca ona etki eder.
- Kod: yeni `ViewModels/DesignSurfaceViewModel.RepeatPreview.cs`, `Controls/DesignCanvas.RepeatPreview.cs`, `Windows/DesignPropertiesWindow.xaml(.cs)`; `MainWindow.xaml` (Design menüsü, View > Repeat View, araç çubuğu toggle), `MainWindow.Layout.cs`, `MainWindow.Shortcuts.cs` (`design.properties`, `view.repeat` = F5).
- Not: repeat ayarları şimdilik oturuma ait görünüm ayarı; `.rugcad` dosyasına yazılmıyor.

## Eraser artık birinci renkle siliyor

- Eraser sol tuşla **foreground (birinci) rengi** boyuyor. İndeksli bir desende "silmek" zaten bir palet indeksi boyamak demek; hangisi olduğuna kullanıcı karar veriyor. Sağ tuş eski davranışı koruyor: ikinci renk.
- Hem fırça hem lasso modunda geçerli; lasso önizlemesindeki dolgu rengi de artık darbenin başladığı renge göre çiziliyor (`_extendedColor`), eskiden sabit BackgroundPaletteIndex'ti.
- Araç yardımı güncellendi.

## Repeat görünümünde pan ve zoom

Repeat açıkken tuval tek bir desen değil, sonsuz bir alan; gezinme kuralları da buna göre değişiyor:

- **Pan serbest**: `ClampPan` repeat açıkken kenara yaslama/ortalama yapmıyor. Bunun yerine, gerçek desen görünümden tamamen çıktığında bir döşeme kadar sarmalıyor (drop da hesaba katılıyor, yani düşümlü raporda komşusuna denk geliyor). Böylece alan sonsuz görünüyor ama kaydırma değerleri sınırsız büyümüyor. Sarma eşiği bilerek viewport kadar geniş: Center/Fit komutları deseni söyledikleri yere koymaya devam ediyor.
- **Zoom tabanı düşüyor**: normalde `MinZoom` "bir desen ekrana sığsın" değerinde; repeat açıkken 0.02'ye iniyor, çünkü raporu değerlendirmek için daha fazla tekrarı aynı anda görmek gerekiyor.
- **Kaydırma çubukları** repeat açıkken devre dışı (ölçülecek sonlu bir içerik yok).
- Repeat açılıp kapandığında `UpdateZoomBounds` + `UpdateScrollBars` yeniden çalışıyor; kapanınca normal sınırlar geri geliyor.
- Döşeme sayısı artık origin çevresinde simetrik halka yerine gerçekten görünür aralıktan hesaplanıyor (`Span`), drop telafisi iki geçişte yapılıyor ve `RepeatTileBudget` (4000) üzerinde önizleme çizilmiyor — çok uzaklaşınca kare başına on binlerce blit yapılmasın diye.

## View > Grid, repeat sınır çizgisi ve repeat kopyalarına çizim

- **Repeat kopyalarına çizim (asıl düzeltme)**: `ToDesignCoordinates` artık repeat açıkken noktayı gerçek desene katlıyor (`FoldRepeatCoordinates`). Ekrandaki hangi kopyaya tıklanırsa tıklansın, o nokta tek gerçek desendeki karşılığına dönüşüyor; döşeme adımı ve drop geri alınıyor. Drop varken satır ve sütun birbirine bağlı olduğu için çözüm birkaç geçişte yakınsıyor, sonda modulo ile sınırlara oturtuluyor. Tek giriş noktası olduğu için bütün araçlar (çizim, seçim, kova, eyedropper...) kendiliğinden bütün raporda çalışıyor. Önceden yalnız ortadaki kopyaya çizilebiliyordu.
- **View > Grid** alt menüsü: Show Grid, Repeat Boundary ve Grid Settings... Üçü de kısayol atanabilir.
- **Grid**: her N desen pikselinde bir kılavuz çizgi, tuvalin tamamında (repeat kopyaları dahil) çiziliyor. Ekranda 4 pikselden sıkışık olacağı durumda çizilmiyor — deseni örtmesin diye.
- **Repeat sınır çizgisi** artık kapatılabiliyor ve rengi seçilebiliyor. Bu çizgi bir sınır değil, yalnızca gerçek desenin nerede olduğunu gösteren işaret.
- **Settings penceresi sekmeli oldu**: "Save As" ve "Grid". Grid sekmesinde grid aç/kapa, aralık, grid rengi, repeat sınırı aç/kapa ve sınır rengi var (10 adet adlandırılmış renk). View > Grid > Grid Settings doğrudan Grid sekmesini açıyor.
- Ayarlar `%AppData%\RugCAD\grid-preferences.json` içinde tutuluyor, tüm açık tasarımlar için ortak (`Services/GridPreferences.cs`). Kılavuz çizgileri canlı uygulanıyor — rengi seçerken etkisini görmek gerekiyor — bu yüzden Cancel açılıştaki değerleri geri yüklüyor.
- Kod: yeni `Services/GridPreferences.cs`, `Controls/DesignCanvas.Grid.cs`; `Controls/DesignCanvas.xaml.cs` (`FoldRepeatCoordinates`, grid çizimi), `Controls/DesignCanvas.RepeatPreview.cs`, `Windows/SettingsWindow.xaml(.cs)`, `MainWindow.xaml`, `MainWindow.Layout.cs`, `MainWindow.Shortcuts.cs`.

- Repeat sınır çizgisi artık **her kopyanın** etrafında çiziliyor, yalnızca gerçek desenin değil: işaretlediği şey bir raporun nerede bitip diğerinin nerede başladığı. Gerçek desen 2 piksel, kopyalar 1 piksel çizgiyle; hangisinin gerçek desen olduğu yine ayırt ediliyor (çizim zaten hepsinde çalışıyor). Çerçeveler artık ayrı bir geçişte (`DrawRepeatBoundaries`), desen bitmap'i çizildikten sonra uygulanıyor — aksi halde desen kendi çerçevesinin üstünü boyuyordu.
- Sınır çizgisi artık her kopyada aynı 1 piksel kalınlıkta; gerçek desen ayrıca vurgulanmıyor.
- Araç çubuğunda Repeat'in **soluna** Grid yuvası eklendi: **Grid** toggle düğmesi + sağında küçük ▾ oku (gruplanmış araç yuvalarıyla aynı düzen). Ok, Show Grid / Repeat Boundary işaretlenebilir maddelerini ve Grid Settings... girişini açıyor. Menü, araç çubuğu düğmesi ve ok menüsündeki kopyaların hepsi `SyncGridMenu` ile tek ortak tercihten besleniyor.

## Bucket: Fill ve Area radyo grubu, Ignore protection kaldırıldı, Pattern dolgusu düzeltildi

- **Pattern dolgusu çalışmıyordu (gerçek hata)**: `ApplyBucketFill`, motif rengini `TryGetDrawingIndex` üzerinden istiyordu; o metot ise araç düzeyindeki "paint from Active Pattern" anahtarı kapalıyken hemen fallback rengi döndürüyor. Kovada bu anahtar kapalı olduğu için Fill: Pattern seçilse bile düz renk basılıyordu. Motif araması `TryGetActivePatternIndex` olarak ayrıldı (koşulsuz) ve kova doğrudan onu çağırıyor; `TryGetDrawingIndex` artık anahtarı kontrol edip bu metoda devrediyor. Motifin deliğine denk gelen piksel boyanmıyor, seçili pattern yokken de hiçbir şey basılmıyor.
- **Fill** ve **Area** açılır listeden radyo düğmesi grubuna çevrildi; her iki grupta da bütün seçenekler aynı anda görünüyor. Enum hâlâ tek doğruluk kaynağı, radyo düğmeleri Curve araç tipindeki gibi bool projeksiyonlara bağlı (`BucketFillActiveColor/Pattern/Spray`, `BucketAreaSingle/All/Unprotected`).
- **Ignore color protection** kaldırıldı: Stop rengi artık koşulsuz korunuyor (`IgnoreProtection = false`). VM özelliği, alan ve Reset'teki kullanımı ile artık kullanılmayan `BucketFillStyles`/`BucketAreas` liste kaynakları da temizlendi.
- Bucket araç yardımı güncellendi.

## Repeat tipleri Texcelle listesine göre yeniden yazıldı

Kullanıcı Texcelle'in Repeat Settings açılır listesini gösterdi. Liste `Texcelle 2009\TexLib61.dll` içinden birebir doğrulandı (aynı 15 madde, aynı sıra) ve `DesignRepeatMode` bu listeye çevrildi:

Simple Design, Straight Repeat, Mirrored Design X & Y, Mirrored Design X & Y (Double Edge), Mirrored Design X, Mirrored Design X (Double Edge), Mirrored Design Y, Mirrored Design Y (Double Edge), Mirror Median, Mirror Pivot, Mirror Median (Running), Mirror Pivot (Running), Half Drop Repeat, Quarter Drop Repeat, Brick Repeat.

- Önceki model (Straight/VerticalDrop/HorizontalDrop/Custom + 1/N kesir) kaldırıldı; o benim uydurduğum bir şemaydı ve Texcelle'in mantığını karşılamıyordu.
- Her tip tek bir `RepeatLayout` kaydına indirgeniyor: `StepX/StepY` (komşu kopya arası mesafe), `DropX/DropY` (satır/sütun kayması), `MirrorX/MirrorY` (tek numaralı sütun/satır aynalanır), `Tiles` (Simple Design'da false).
- **Aynalama**: düz aynalı tipler kenar çizgisini paylaşır (adım = genişlik − 1); **(Double Edge)** tam genişlik adımıyla kenar çizgisini iki kez gösterir.
- **Half/Quarter Drop** her sütunu yüksekliğin yarısı/çeyreği kadar düşürür; **Brick** her satırı genişliğin yarısı kadar kaydırır.
- Dialogdaki Drop X / Drop Y artık *ek* kayma: seçilen tipin kendi düzenine ekleniyor, böylece her tip ayrı bir madde gerektirmeden kaydırılabiliyor.
- Aynalı kopyalar `canvas.Scale(-1,…)` ile çiziliyor ve **`FoldRepeatCoordinates` aynalamayı da geri alıyor** — aynalı bir kopyaya çizilen darbe desende aynanın söylediği yere iniyor.
- Pan sarması artık desen boyutuna değil repeat adımına göre; aynalı tiplerde iki adımda bir sarıyor ki görüntü sararken ters dönmesin. Zoom tabanı ve kaydırma çubuğu kuralları da yalnızca gerçekten döşeyen tiplerde devreye giriyor (Simple Design normal davranışta kalıyor).

**Doğrulanması gereken:** Mirror Median / Mirror Pivot ve bunların (Running) biçimleri için Texcelle'in tam kuralı DLL'de yazılı değil. Şu anki yorum: Median deseni kendi orta eksenlerinde aynalıyor (kenar paylaşımlı), Pivot köşe noktası etrafında döndürüyor (kenar çift), Running ikisine yarım düşüm ekliyor. Kullanıcı Texcelle'de karşılaştırıp düzeltmeli.

- **Sınırlı (bounded) raporlar**: aynalı tipler artık sonsuz döşenmiyor. Tek eksen aynalı = **2 kopya**, iki eksen aynalı = **4 kopya** (Mirror Median ve Mirror Pivot dahil); görünüm tam olarak o bloğu gösterip duruyor. `RepeatLayout.Columns/Rows` (0 = sonsuz) ve `RepeatLayout.Bounded` bunu taşıyor. Straight, Half/Quarter Drop ve Brick sonsuz kalıyor; (Running) biçimleri de düşümle akıp gittiği için sonsuz.
- Sınırlı raporda gezinme normal desen kurallarına dönüyor: `RepeatExtent` (bloğun desen pikseli cinsinden boyutu) pan sınırlaması, kaydırma çubukları, Fit to Window ve zoom tabanı için içerik boyutu olarak kullanılıyor — yani Fit bütün bloğu gösteriyor. Sonsuz raporlarda serbest pan + sarma davranışı korunuyor.
- Blok dışına tıklamak desen dışına tıklamakla aynı: `FoldRepeatCoordinates` blok dışındaki noktayı katlamıyor, olduğu gibi bırakıyor ve normal sınır kontrolü eliyor.

## Ayarların kapanışta sıfırlanması — tek ortak ayar dosyası

Kullanıcı klavye ayarlarının ve bazı tercihlerin uygulama kapanıp açılınca sıfırlandığını bildirdi.

- **Asıl hata — `KeyboardShortcutStore.Load` hep ya da hiç çalışıyordu.** Kaydedilmiş tek bir girdi herhangi bir kontrolü geçemezse (ayrıştırılamayan tuş, iki komutun paylaştığı bir kısayol, tanınmayan değiştirici) metot `return` edip **bütün dosyayı** yok sayıyordu; kullanıcının ayarladığı her kısayol sessizce varsayılana dönüyordu ve nedenini gösteren hiçbir şey yoktu. Artık girdi girdi okunuyor: geçerli olan korunuyor, yalnızca uygulanamayan atılıyor. Çakışmada komut sırası kazanıyor (editörün atama kuralıyla aynı).
- **Tek dosya**: `Services/AppSettings.cs` ile bütün kalıcı tercihler `%AppData%\RugCAD\settings.json` içinde tek bir belgede toplandı — kısayollar, Save As türü, grid/repeat sınırı ayarları, Recent listesi ve View anahtarları. Belge bütün hâlinde ve atomik yazılıyor; okunamayan bir bölüm yalnızca kendi varsayılanına düşüyor, dosyanın kalanını götürmüyor.
- **Eski dosyalar otomatik taşınıyor**: settings.json yoksa `shortcuts.json`, `save-as-preferences.json`, `grid-preferences.json` ve `recent-designs.json` bir kez okunup içeri alınıyor, kimse mevcut ayarını kaybetmiyor.
- **Yeni kalıcı hâle gelenler**: View > Scrollbars ve Selection Display seçimi. Bunlar yalnızca bellekte duruyordu, her açılışta varsayılana dönüyordu.
- Hâlâ ayrı duranlar: `layout.xml` (AvalonDock'un kendi biçimi) ve `patterns.json` (motif kütüphanesi, bir tercih değil veri).
- **Kalıcı olmayan:** araç ayarları (fırça boyutu, bucket/curve/airbrush seçenekleri) hâlâ oturuma ait. İstenirse `AppSettings`e bir Tools bölümü eklenebilir.

## MDI çalışma alanı (Aşama 1) — Texcelle gibi yan yana desen pencereleri

Kullanıcı desenlerin sekme yerine Texcelle'deki gibi yan yana pencereler olarak açılmasını, pencereler arasında sürükle-bırak yapılabilmesini ve paletler farklıysa Texcelle'deki palet birleştirme akışının çıkmasını istedi.

**Texcelle'den çıkarılanlar** (`texcelle.hlp` / `texcelle.cnt` içerik listesi ve dizin dizeleri):
- Window menüsü: **New Window, Cascade, Vertical, Horizontal, Arrange Icons, Open Windows Area**.
- Edit menüsü: **Paste Special...**
- **Colour Handling Wizard** — farklı paletli içerik geldiğinde çıkan sihirbaz. Yardım dizininden çıkan seçenekleri: **"Use the new colour palette"**, **"Add colours from index"**, **"Add colours to non-used indexes"**, Finish/Cancel/Help.

**Aşama 1 — bu commit'te yapılan:** desenler artık ortadaki çalışma alanında birer alt pencere.
- Yeni `Controls/MdiChild.cs`: başlık çubuğundan taşınan, kenarlarından boyutlanan, küçült/büyült/kapat düğmeleri olan desen penceresi. Aktif pencerenin başlık çubuğu vurgulu.
- Yeni `Controls/MdiHost.cs`: pencereleri tutan çalışma alanı. `Cascade`, `TileHorizontal`, `TileVertical`, `ArrangeIcons` (küçültülmüşleri alta dizer), aktif pencere takibi, yeni pencereyi basamaklı açma, yeniden boyutlandırmada pencereleri ekranda tutma.
- Yan paneller AvalonDock'ta kaldı; çalışma alanı, belge alanına konan tek bir `LayoutDocument` (`Workspace`) içinde duruyor. Kaydedilmiş düzen geri yüklenirken bu belge yeniden yerleştiriliyor (`RestoreWorkspaceDocument`), yoksa çalışma alanı kaybolurdu.
- `OpenDocumentTab` artık `Workspace.Open`, `ActiveCanvas()` öndeki pencere, Window menüsündeki açık desen listesi ve Home sayfasının görünürlüğü çalışma alanından besleniyor.
- Window menüsü Texcelle'e göre yeniden düzenlendi: **New Window** (aynı desene ikinci görünüm; aynı ViewModel, ayrı zoom/konum), Cascade, Tile > Horizontal/Vertical, Arrange Icons, Send to Bottom (aktif pencereyi küçültür). Eskisi olan "Tabs (single pane)" kaldırıldı; AvalonDock pane'lerini böl-birleştir eden eski `MainWindow.Tiling.cs` mantığı yerini tek satırlık çağrılara bıraktı.

**Sıradaki aşamalar:**
- Aşama 2: pencereler arasında seçim sürükle-bırak.
- Aşama 3: Colour Handling Wizard — hedef desenin paleti farklıysa üç seçenekle (yeni paleti kullan / verilen indeksten itibaren renk ekle / kullanılmayan indekslere ekle) gelen içeriği yerleştirme. Paste Special da aynı akışı kullanacak.

## Colour Handling Wizard ve sabit 256 palet

- `Palette` çekirdeği ve Color Palette UI'ı artık her zaman 256 slot (`0..255`) taşır. Kısa paletle gelen dosyalar siyah ile doldurulur; kullanılmayan indeksler görünür ve hedef olarak kullanılabilir.
- Farklı paletli selection/motif başka bir MDI desene bırakıldığında **Color Handling Wizard** açılır. Mevcut indeksleri kullanma, gelen paleti kullanma, RGB karşılığını bulma, renkleri kullanılmayan indekslere ekleme ve elle overlap table tanımlama seçenekleri uygulanır.
- `Edit > Paste Special...` ortak clipboard'dan gelen seçim için aynı sihirbazı kullanır. Cancel hedef deseni değiştirmez; Help seçeneklerin davranışını açıklar.

## Pencere odağı, kaydetme koruması ve About

- Tekli veya çoklu Open sonrasında en son açılan MDI desen etkinleştirilir ve en öne getirilir.
- Değişen desen başlığında `*` görünür. Son görünümü kapatırken ya da uygulama kapanırken Save / Don't Save / Cancel sorulur; yeni dosya için Save As açılır.
- Tek ortak alt çubuk solda toolbar bağlam yardımını, ortada aktif desen/zoom bilgisini, işlem sırasında I/O ilerleyicisini ve sağda `RugCAD • v1.0.0 • YLDRMTECH` bilgisini gösterir.
- Help > About RugCAD, YLDRMTECH bilgisi ve `Assets/rugcad.png` marka görselini gösterir. Ana uygulama penceresi taskbar/title icon için ayrı PNG zorlamaz; Windows, projedeki şeffaf `ApplicationIcon` olan `Assets/rugcad.ico` ikonunu kullanır. About, Settings ve renk/seçim gibi tüm yardımcı pencereler tool-window olarak açılır; başlıklarında uygulama ikonu ve görev çubuğu girişi bulunmaz.

## Menü ikonları ve görünüm anahtarları

- Tüm menü komutları solda bir ikon alanı taşır. New, Open ve Save komutları File menüsünde ve ana toolbar'da aynı Segoe MDL2 Assets ikonlarını kullanır.
- Grid, Repeat Boundary, Repeat View, Scrollbars ve seçim görünüm seçenekleri etkin olduğunda sol ikon alanında `✓` görünür. Paper Format için geometrik kâğıt/piksel oranı, Repeat için döngü oku ikonu toolbar'a eklendi.
- Menüde okunabilir işlevi olmayan genel `.` ikonları kaldırıldı. Grid (`▦`), Paper Format (`▧`) ve Repeat (`↗`) araç çubuğunda Texcelle'in kompakt teknik simge diline yakın, metinsiz toggle düğmeleri olarak görünür; açıklamaları tooltip'tedir.

## Design > Graphics Repeat

- `Design > Graphics Repeat...` Texcelle'deki Repeats ekranının kompakt karşılığıdır: Orientation alanında Straight, Half Drop, Quarter Drop veya Brick seçilir; Drop alanında ekstra Horizontal/Vertical piksel kayması girilir.
- OK repeat yerleşimini ve istenirse repeat preview'ü uygular, piksel verisini değiştirmez. Cancel hiçbir değişiklik yapmaz. Ayrıntılı Texcelle repeat türleri Design > Properties altında kalır.
- Design > Graphics Repeat menü simgesi Texcelle tarzı çift mavi yön oku oldu; ana araç çubuğundaki Repeat View toggle ise turuncu/kırmızı/sarı dört hücreli repeat işaretini kullanır.

## Texcelle uyumlu Sort Colors

- Sort Colors her açılışta **Original** modunda açılır. Ascending/Descending yalnızca Statistics ve Intensity modlarında etkin olur; Original, Reverse ve Manual'da Sort order alanı Texcelle'deki gibi pasiftir.
- Swatch'lar sürükle-bırak ile istenen konuma taşınabilir; yukarı/aşağı düğmeleri de korunur. Her iki elle düzenleme yöntemi otomatik olarak **Manual** modunu seçer ve mevcut sıralamayı korur. `Lock 0 index` açıkken 0. renk sürüklenemez veya yer değiştiremez.

## Kaydedilmemiş işareti ve aynı dosyayı yeniden açma

- MDI başlığındaki `*` bir boolean akış artığı değil, gerçek kaydedilmiş snapshot ile kıyaslanan dirty durumudur. Açık dosya ilk yüklendiğinde veya Save başarılı olduğunda yıldız yoktur; tüm düzenlemeler Undo ile kaydedilen haline dönünce yıldız otomatik kalkar.
- Açık bir dosyanın `File > Open` ile aynı yolu yeniden seçilirse ve açık sürümde kaydedilmemiş değişiklik varsa kullanıcıya bu değişiklikleri kaydetmeden açık tasarımı kapatıp disk sürümünü yeniden açmak isteyip istemediği sorulur. Yes tüm aynı-tasarım MDI görünümlerini kapatır ve disk sürümünü açar; No mevcut pencereyi öne getirir.

## Repeat görünümünde raport kenarı düzenleme

- Repeat View açıkken brush/kalem, line ve diğer şekil araçları ile rectangular, ellipse ve lasso selection sürüklemeleri bir repeat kopyasının kenarından komşu kopyaya kısa bir hareket olarak yapılabilir.
- Sürükleme ekranın sanal repeat koordinatında korunur; işlem uygulanırken her piksel mevcut repeat/drop/mirror yerleşimine göre ana tasarıma katlanır. Böylece kenar birleşiminde yapılan raport düzeltmesi iki tarafta aynı anda görünür; araç ortadan dolaşan uzun bir çizgi veya seçim üretmez.

## Texcelle tarzı ortak alt bilgi çubuğu

- Ana pencerenin tek alt çubuğu artık desen adı ve boyutunu tekrar etmez. Solda toolbar üstüne gelindiğinde komut yardımı, sağa yakın ikonlu alanlarda aktif MDI deseninin imleç koordinatı, paletteki renk kutusu/indeksi, seçim boyutu, fiziksel sürükleme açısı ve zoom bulunur.
- Renk/piksel bilgisi imleç canvas üzerindeyken; seçim boyutu selection araçlarında; açı ise selection, line, gradient ve border sürüklenirken canlı güncellenir. Arka plandaki MDI pencerelerinin olayları öndeki pencerenin bilgisini değiştiremez.
- I/O sırasında ilerleyici, en sağda ise `YLDRMTECH.com • RugCAD v1.0.0` görünür.

## MDI kullanım düzeltmeleri ve fiziksel açı bilgisi

- Seçim marquee/ellipse/lasso ile Line, Gradient ve Border sürüklenirken alt bilgi satırında `∠` açı görünür. Açı, ham piksel oranı yerine `dx / warp` ve `dy / weft` üzerinden hesaplanır; Paper Format'taki fiziksel oranla uyumludur.
- Tile Horizontal veya Vertical çağrısında tam üç açık pencere varsa boş 2x2 alan bırakılmaz: aktif pencere solda büyük, diğer ikisi sağda üst/alt küçük yerleşir. Aktif pencere her tile/cascade sıralamasında ilk sıradadır.
- MDI alt pencere olayları handled olsa bile arka plandaki canvas'a tıklama veya araç işlemi pencereyi etkinleştirip öne getirir.
- Aynı dosya yolu yeniden açıldığında ikinci normal belge oluşturulmaz; mevcut pencere öne gelir. Diskteki tasarım ile çalışma alanındaki tasarım piksel, palet, Park/Stop veya Paper Format bakımından farklıysa kullanıcı disk sürümünü ayrı açmayı seçebilir.

## MDI yerleşim ve ortak durum çubuğu

- Pencere sayısı tam dikdörtgen ızgara oluşturmuyorsa etkin pencere featured sütunda büyük tutulur; kalan `N-1` pencere yanındaki tam ızgaraya yerleşir. Böylece boş hücre kalmaz.
- Küçültülmüş pencereler çalışma alanının altındaki ayrı koyu rafta yan yana, sığmazsa satır satır düzenlenir.
- Başlık çubukları etkinlik durumuna göre mavi ya da gri gradient kullanır.
- Her alt pencerenin durum çubuğu gizlendi; seçili tasarımın boyut/Paper Format ve zoom bilgisi ana çalışma alanının tek ortak durum çubuğunda gösterilir.
- MDI başlık düğmeleri daha büyük, beyaz ikonlu ve çerçeveli; Ctrl+Tab açık desenleri canlı önizleme kartlarıyla gösterir. Ctrl basılıyken Tab/Shift+Tab ile dolaşılır, Ctrl bırakılınca seçilen pencere etkinleşir.

## Graphics Repeat penceresi Texcelle'in gerçek diyaloğuna göre yeniden yazıldı

`TexLib61.dll` içindeki dialog şablonları PE kaynaklarından ayrıştırıldı. Texcelle'in **"Repeats"** diyaloğunun gerçek içeriği:

```
Orientation (groupbox)          Drop (groupbox)
  RepeatView (owner-draw)         Horizontal: [edit]
  yatay scrollbar (altında)       Vertical:   [edit]
  dikey scrollbar (sağında)
                                        [OK] [Cancel]
```

- Yani "Orientation" bir radyo düğmesi kümesi değil: **canlı bir repeat önizlemesi ve onu kaydıran iki scrollbar**. Scrollbar'lar drop'u ayarlıyor, Drop kutularındaki sayılar da aynı değeri gösteriyor — tek ayar, iki görünüm.
- `GraphicsRepeatWindow` buna göre yeniden yazıldı: önizleme deseni gerçek palet/piksellerle döşüyor (ayna ve drop dahil), birleşim yerlerine ince bir çizgi koyuyor; scrollbar ↔ sayı iki yönlü; Cancel drop'u geri alıyor. Repeat **tipi** Texcelle'deki yerinde, Design > Properties'te kalıyor (orada da "Repeat settings: Type, Drop X, Drop Y" var — DLL'den doğrulandı).
- Araç çubuğunda Repeat düğmesinin soluna kendi ikonuyla Graphics Repeat düğmesi eklendi.

## Repeat açıkken çizim/seçim bozuluyordu (gerçek hata)

- Sürüklemeli araçlar için "repeat edit path" mekanizması vardı ama **yalnızca sağ tuş / extended tool yolunda** başlatılıyordu. Sol tuşla yapılan her sürükleme (kalem, şekil, seçim, gradient, border, move) her fare hareketini ayrı ayrı katlıyordu; iki örnekleme raporun iki farklı kopyasına düşünce koordinat birden atlıyor ve desenin bir ucundan diğerine düz çizgi çiziliyordu. Seçim de aynı sebeple kurulamıyordu.
- `OnMouseLeftButtonDown` artık sürüklemeli araçlarda ham (katlanmamış) koordinata geçip `StartRepeatEditPath` çağırıyor; bitmiş şekil/seçim eskisi gibi `FoldRepeatEditPoints` ile tek desene katlanıyor. Böylece dikişin üzerinden geçen kısa bir sürükleme kısa kalıyor.
- Seçim isabet testi (`IsPixelSelected`) katlanmış noktayla yapılıyor: hangi kopyadan tutulursa tutulsun seçim taşınabiliyor.
- Kalem, örneklemeler arası çizgiyi ham uzayda kuruyor ama her pikseli basarken katlıyor (`PaintRepeatAwarePixel`).

## Color Handling Wizard seçenekleri çalışmıyordu (gerçek hata)

- **"Use the current color palette (nearest matching colors)"** hiçbir şey yapmıyordu: kod indeksleri olduğu gibi geçiriyordu, yani motif hedef paletin o indekslerdeki renkleriyle geliyordu — etiketin söylediğinin tam tersi. Artık her gelen renk hedef paletteki **en yakın** renge eşleniyor, palet değişmiyor.
- "Add incoming colors to unused palette indexes" artık **indeks 0'ı dağıtmıyor** (arka plan rengi); palet dolduğunda en yakın renge düşüyor.
- "Find the corresponding color" ortak renkleri koruyup kalanlar için en yakına düşüyor; "Use the new color palette" ve "Define overlap table" aynen duruyor.
- Handbook'a Editing > Color Handling Wizard ve Menus > Design > Graphics Repeat konuları eklendi.

- Graphics Repeat penceresi büyütüldü: 980x720 varsayılan, yeniden boyutlandırılabilir/tam ekran yapılabilir, önizleme pencereye göre büyüyor. Kaç kopyanın ekrana sığacağını belirleyen bir zoom kaydırıcısı eklendi (1–12).
- Texcelle'deki gibi **yön okları** eklendi: sol/sağ/yukarı/aşağı repeat'i adım adım kaydırıyor (tık = 1 piksel, Shift = 10, Ctrl = tam bir repeat), ortadaki daire drop'u sıfırlıyor. Oklar, scrollbar'lar ve Drop kutuları aynı değeri paylaşıyor.

## Repeat'te seçim tek kopyada kalıyordu (gerçek hata)

- Seçim doğru kuruluyordu (koordinatlar desene katlanıyor) ama **seçim göstergesi yalnızca desenin kendi kopyasının üzerine çiziliyordu**. Dolayısıyla kopyaların üzerinden, dikişleri aşarak yapılan bir seçim o tek kopyaya katlanıp kırpılmış gibi görünüyordu.
- `DrawSelectionOnRepeatCopies` eklendi: seçim göstergesi görünen her kopyaya, o kopyanın kendi dönüşümüyle (kaydırma + aynalı kopyalarda yansıma) yeniden çiziliyor. Tek seçim, tek desen — ama rapordaki her tekrarında görünüyor, dolayısıyla kullanıcının çizdiği yerde de görünüyor.
- Handbook'taki Navigation > Repeat view maddesi güncellendi.

## Hata olunca uygulama kapanmasın

- `App.xaml.cs` artık `DispatcherUnhandledException` yakalıyor: hata olan işlem durduruluyor, kullanıcıya ne olduğunu söyleyen bir uyarı gösteriliyor ve **oturum devam ediyor**. Açık tasarımlar olduğu gibi kalıyor, kaydedilmemiş çalışma kaybolmuyor. WPF'in varsayılanı bu durumda uygulamayı kapatmaktı — bir araçtaki hatanın saatlerce süren desen çalışmasını götürmesi, o aracın işi bitirememesinden çok daha kötü bir sonuç.
- Ayrıntılar `%AppData%\RugCAD\errors.log` dosyasına zaman damgasıyla ekleniyor. `AppDomain.UnhandledException` ve gözlenmemiş Task hataları da loglanıyor.
- Repeat'te seçimin çökme sebebi de sınırlandı: repeat görünümünde seçim **görüntülenen alanda** ölçülüyor ve o alanın kenarı yok; uzaklaşmışken tek bir dikkatsiz sürükleme yüz milyonlarca piksellik bir alan kaplayabiliyor, rasterleyiciler de o boyutta bir ızgara ayırmaya çalışıp belleği tüketiyordu. Artık 40 milyon pikseli aşan marquee/lasso hareketi uygulanmadan bırakılıyor ve durum çubuğu "selection too large" yazıyor (sessizce küçültmek, kullanıcının çizmediği bir yere seçim koymak olurdu).

## Kısayol atamasının açılışta geri dönmesi (gerçek hata)

- `KeyboardShortcutStore.Load` içinde iki "eski sürümden geçiş" satırı vardı: Deselect'te `Escape` görürse `Ctrl+D` yapıyor, Rectangular Marquee'de `K` görürse `M` yapıyordu. Tek seferlik bir yükseltme olarak yazılmışlardı ama "yapıldı" diye işaretlenmedikleri için **her açılışta** çalışıyorlardı. Sonuç: kullanıcı Rectangular Marquee'ye bilerek `K` atayınca, program bir sonraki açılışta onu `M`'ye çeviriyordu ve atamayı kalıcı yapmanın yolu yoktu.
- Her iki satır da kaldırıldı. Kaydedilmiş atama kullanıcının kararıdır; artık hiçbir şey onu yeniden yazmıyor.

## Graphics Repeat: Orientation gerçekte ne demekmiş

Kullanıcı Texcelle'in ekran görüntüsünü gönderdi: **Orientation**, 2×2'lik bir **ok ızgarası** — her kare raporun o çeyreğindeki kopyanın hangi yöne baktığını gösteriyor. Dördü de aynı yöne bakıyorsa düz rapor; yandaki kopya ters dönmüşse rapor soldan sağa aynalı; alttaki ters dönmüşse yukarıdan aşağı aynalı; çaprazdaki ters dönmüşse iki eksende de aynalı. Arka plandaki halı görselinde bordürün birleşim yerlerinde kendisiyle buluşması tam olarak bu.

- Önceki yorumum (Orientation = radyo düğmeleri / önizleme kutusu) yanlıştı. Artık dört çeyrek ok düğmesi var; bir çeyreğe tıklamak o ekseni aynalıyor, ilk çeyreğe tıklamak hepsini düz rapora döndürüyor. Oklar `ScaleTransform` ile çevrilerek çiziliyor, yani blok bir ayar listesi değil raporun resmi gibi okunuyor.
- Seçim doğrudan `RepeatMode`'a yazılıyor: yok → Straight Repeat, X → Mirrored Design X, Y → Mirrored Design Y, ikisi → Mirrored Design X & Y. Design > Properties'teki tip listesiyle aynı değer.
- Önizlemede **sol tık yakınlaştırma, Alt+sol tık uzaklaştırma** (1–16 kopya arası). Zoom kaydırıcısı kaldırıldı, yerini bu aldı.
- Önizleme artık çerçevenin kendi boyutundan ölçülüyor; eskiden Image'ın boyutunu soruyordu, o da bitmap'e göre kendini ayarladığı için önizleme kutuyu doldurmuyordu (kullanıcının gönderdiği görselde alttaki boş gri alan).
- Cancel artık Orientation değişikliğini de geri alıyor.

## Repeat'te seçim gösterimi (Texcelle karşılaştırması) ve gradient

Kullanıcı Texcelle ile bizim ekranı yan yana gönderdi. Texcelle seçimi **çizildiği yerde tek bir dikdörtgen** olarak gösteriyor; dikişi aşan seçim iki kopyaya yayılmış tek bir dikdörtgen gibi duruyor.

- Önceki çözüm (seçimi **her** kopyaya çizmek) fazla geniyordu: seçim rapor boyunca tekrar ediyor, kullanıcının çizdiği dikdörtgen kaybolup gidiyordu.
- Artık sürüklemenin rapor alanındaki dikdörtgeni saklanıyor (`_repeatSelectionField`) ve seçim göstergesi **yalnızca o dikdörtgenin dokunduğu kopyalara**, üstelik ekran uzayında o dikdörtgene kırpılarak çiziliyor. Dikişi aşan bir marquee'nin iki yarısı birleşim yerinde buluşuyor ve tek dikdörtgen olarak okunuyor. Altta yatan seçim yine tek desende, tek seçim.
- Seçim kalktığında bu dikdörtgen de unutuluyor.

### Gradient donması (gerçek hata) ve rapor boyunca devam etmesi

- Repeat açıkken gradient sürüklendiğinde iki uç **ayrı ayrı** katlanıyordu; uçlar rapor alanında yüz binlerce piksel uzağa düşebildiği için hem yön bozuluyor hem de işlem uygulamayı kilitlenmiş gibi gösterecek kadar uzuyordu.
- `FoldRepeatEditVector` eklendi: başlangıç normal katlanıyor, bitiş **aynı miktarda** kaydırılıyor. Böylece çizilen doğru olduğu gibi korunuyor — bir kopyadan diğerine çekilen gradient, çizildiği yön ve uzunlukla desene uygulanıyor ve rapor boyunca devam ediyor.
- Seçim yokken gradient başlangıç noktasının altındaki bölgeyi doldurur; o nokta desenin dışındaysa (kopyadan başlatılmışsa) artık tüm desen hedef alınıyor, araç sessizce hiçbir şey yapmıyor durumda kalmıyor.

## Repeat'te curve/polyline çizilemiyordu (gerçek hata — iki ayrı sebep)

1. **Katlama yoktu**: `CommitCurve` ve `CompletePath`, `PathPixels(...)` sonucunu doğrudan boyuyordu. Repeat açıkken yol rapor alanında çiziliyor, yani pikseller desenin dışına düşüyor ve `PathPixels` içindeki sınır filtresi onları sessizce atıyordu — Apply'a basınca hiçbir şey olmuyordu. `PathPixels` artık isteğe bağlı `fold` parametresi alıyor: boyamadan önce katlıyor, önizleme ise katlanmamış hâli kullanmaya devam ediyor (imlecin altında çizilmesi gerekiyor).
2. **Nokta toplarken raw mod kapanıyordu**: curve/polyline tıklama tabanlı ve her tıktan sonraki mouse-up `EndStroke`'tan geçiyor, o da `EndRepeatEditPath()` çağırıyordu. Sonuç: ilk nokta tıklandığı kopyanın koordinatında, sonrakiler desene katlanmış — yani figür kimsenin çizmediği bir yere gidiyordu. `EndRepeatEditPathUnlessPathPending` eklendi: bir yol aracı hâlâ nokta topluyorsa hareket kapatılmıyor.
- Polygon Selection'ın Enter/sağ tık ile tamamlanması da repeat-farkında hâle getirildi (lasso ile aynı sanal sınır kutusu yöntemi, boyut sınırı ve çizilen dikdörtgenin hatırlanması dahil).

## Mimari düzeltme: repeat açıkken TÜM rapor tek desendir

Kullanıcının koyduğu teşhis doğruydu ve şimdiye kadarki tek tek düzeltmelerin hepsinin altında yatan sebep buydu: **araçlar hâlâ "desen = tek kopya" varsayıyordu.** Koordinatlar girişte katlanıyordu ama araçların kendi sınır kontrolleri (0..W, 0..H) yerinde duruyordu; dolayısıyla desenin kendi kenarına gelen her şey kırpılıyordu. Seçimin kırpılması, gradient'in yön değiştirmesi, curve'ün hiç boyamaması — hepsi aynı sebebin belirtileriydi.

Çözüm, tek tek araçlarda değil merkezde:

- `DesignSurfaceViewModel.FoldRepeatPoint(x, y)` eklendi — rapor alanındaki herhangi bir noktayı, onu gösteren depolanmış desen noktasına çeviriyor (adım, drop ve ayna dahil; sınırlı raporlarda blok dışı nokta gerçekten dışarıdadır ve öyle kalıyor). Kural artık raporun sahibi olan view model'de, tek bir yerde.
- **Piksel ve maske yazan her nokta** bu kuraldan geçiyor: `PaintIndexedPixels`, `PaintStrokePoints`, `PaintPixel` ve `ApplySelectionPoints`. Desenin dışına düşen bir nokta artık atılmıyor, raporun onu koyduğu yere yazılıyor.
- `DesignCanvas.FoldRepeatCoordinates` artık kendi kopyasını tutmuyor, view model'e devrediyor — isabet testleri ile boyama aynı tanımı kullanıyor.
- Sonuç: repeat açıkken araçlar rapor alanını tek büyük desen gibi görüyor. Kaç kopya öteden ya da dikişin tam üzerinden çalışılırsa çalışılsın normal bir desene çiziyormuş gibi davranıyor; üretilen şey her kopyanın gösterdiği tek desene geri yazıldığı için aynı anda hepsinde görünüyor.

### Kalan iki sınırlama: nokta kırpma ve seçim taşıma

Merkezi katlama tek başına yetmedi, çünkü bazı araçlar noktayı **desenin sınırına kırpıyordu**; katlama doğru çalışsa bile nokta zaten desenin içine çekilmiş oluyordu:

- `Curve`, `Polyline` ve `PolygonSelection` her tıklanan noktayı `Math.Clamp(x, 0, Width-1)` ile desene hapsediyordu. İki kopya ötede tıklanan bir eğri noktası desenin kenarına sürükleniyordu — kullanıcının gönderdiği ekran görüntüsünde noktaların dikiş boyunca yığılmasının sebebi buydu. Aynı kırpma yol araçlarının ilk kopyadan hiç çıkamamasına da yol açıyordu. Yeni `ClampToDesign` yardımcısı repeat açıkken kırpmıyor: alanın kenarı yok, nokta zaten çizim anında katlanıyor. `_pathHover` (önizleme) ve eğri noktasının ok tuşlarıyla kaydırılması da aynı yardımcıya bağlandı.
- `MoveSelectionBoundary` taşınan seçimi desen sınırında kesiyordu: kenarı geçen kısım kayboluyordu. Artık hedef nokta katlamadan geçiyor, yani repeat açıkken seçim bir kopyanın kenarını geçince diğerine devam ediyor.

### Seçim gösterimi ve araç önizlemeleri (kullanıcının 10/11 numaralı görselleri)

- **Seçim kırpılmış görünüyordu.** Gösterim katlanmış maskeden çiziliyordu; dikişi aşan bir marquee o maskede iki şerit (dikişin iki yanındaki parçalar) olduğu için kullanıcı çizmediği hâlde ikiye bölünmüş bir seçim görüyordu. Artık çizilen şekil neyse o gösteriliyor: `DrawRepeatFieldSelection` rapor alanındaki dikdörtgeni doğrudan çiziyor (tint ve marching-ants dahil), maske izleme yalnızca böyle bir şekil yokken devreye giriyor. Şerit hâli tek deseni saklamanın iç detayı; kullanıcıya gösterilecek bir şey değil.
- **Ölçek tutamakları** da artık çizilen dikdörtgeni izliyor (`SelectionFrame`), katlanmış maskenin sınırlarını değil — aksi hâlde tutamaklar kimsenin çizmediği bir yere düşüyordu.
- **Araç önizlemeleri her kopyada görünüyor.** Bir aracın desene koyacağı şey her kopyada görüneceğine göre önizlemesi de öyle olmalı; yalnızca çizildiği kopyada görünen bir eğri önizlemesi raporun nasıl okunacağı hakkında hiçbir şey söylemiyordu. `DrawOnRepeatCopies` ile eğri tutamakları/önizlemesi, şekil önizlemesi ve genişletilmiş araç önizlemesi görünen bütün kopyalara çiziliyor.

## Resize Design: kenar dağıtımı artık boyutu belirliyor, girişler numeric

- **Gerçek hata:** kenar kutularına `-1` yazınca karşı kenar farkı emiyordu — sağdan 1 piksel kesmek isterken sola 1 piksel ekleniyor, desen hiç küçülmüyordu. Model tersine çevrildi: kenar kutuları artık öncelikli, yeni genişlik/yükseklik `eski + sol + sağ` olarak hesaplanıp Pixels X/Y'ye yazılıyor. Boyutu doğrudan yazarsan değişim eskisi gibi anchor'a göre kenarlara dağıtılıyor (iki yön de çalışıyor, `_suppressSync` ile birbirini ezmiyor).
- **Numeric girişler:** yeni `Controls/NumericUpDown` denetimi — `NumericTextBox` + görünür ▲▼ döndürme düğmeleri (RepeatButton, basılı tutunca devam eder). Pixels X/Y ve Warp/Weft bu denetime geçirildi, alt/üst sınırlar tanımlandı. Ok tuşlarıyla adımlama zaten vardı ama görünmüyordu; bilmeyenin sadece üzerine yazması gerekiyordu.
- Handbook'a Menus > Design > Resize konusu eklendi.

## Repeat'te marquee artık rapordan motif kaldırıyor

Kullanıcı iki görselle gösterdi: dikişi aşan bir dikdörtgen seçti, program ona desenin yarısını kaplayan bambaşka bir seçim verdi. İstediği ise **seçtiği parçayı bir pattern gibi almak**.

- Sebep: seçim depolanmış desene katlanıyordu. Dikişi aşan bir dikdörtgen o desende iki ayrı şeride denk geliyor, o şeritlerin sınırlayıcı kutusu da neredeyse tüm deseni kaplıyor — kimsenin çizmediği bir şekil.
- Artık repeat açıkken dikdörtgen/eliptik marquee (Replace modunda) `LiftRepeatFieldRegion` ile **rapor alanını doğrudan okuyor**: işaretlenen dikdörtgenin her pikseli onu gösteren kopyadan örnekleniyor ve sonuç, işaretlendiği yerde duran yüzen bir motif olarak taşınıyor. Bir kopyanın parçasıyla diğerinin parçası tek parça hâlinde geliyor.
- Desende hiçbir şey değişmiyor; sürükleyip yerleştirilebiliyor, Enter uyguluyor, Escape bırakıyor. Yerleştirme sırasında pikseller zaten merkezi katlamadan geçtiği için desene doğru yazılıyor.
- Ekle/çıkar/kesiştir modları ve diğer seçim araçları eski davranışlarında kalıyor.

## Gradient dikişte kesiliyordu (gerçek hata)

- `ApplyDraggedGradient` rampayı **yalnızca desenin kendi dikdörtgeni** üzerinde üretiyordu (`DrawingTools.Gradient(Document.Width, Document.Height, ...)`). Bir sonraki kopyaya uzanan sürüklemede rampanın desen dışına düşen kısmı hiç üretilmiyordu; gradient birleşim yerinde bitiyordu (kullanıcının 13 numaralı görseli).
- `RepeatFieldGradient` eklendi: rampa artık **sürüklemenin geçtiği alan** üzerinde kuruluyor (ızgara hem sürüklemeyi hem desenin kendi kopyasını kapsıyor), sonra her piksel merkezi katlamadan geçiriliyor. Böylece bir sonraki kopyaya düşen kısım o kopyanın gösterdiği yere iniyor ve gradient dikişin ötesine devam ediyor.
- 40 milyon piksellik üst sınır var; çok uzaklaşmış bir sürükleme uygulamayı kilitlemiyor.

- Kaldırılan motifte **sekiz tutamak** geri geldi: rapordan bir parça kaldırıldığında düz bir desende çizilen marquee'deki gibi tutamaklar açılıyor, parça yerleştirilmeden önce boyutlandırılabiliyor. Ayrıca `PlacePatternSelection` artık `keepTool` seçeneğiyle çağrılıyor — kaldırma işleminden sonra araç Move'a atlamıyor, marquee elde kalıyor ki hemen başka bir parça işaretlenebilsin. (Active Pattern bırakıldığında Move'a geçme davranışı aynen duruyor; orada yapılacak tek şey yerleştirmek.)

## Başka kopyada seçim yapınca öncekinin iptal olması (gerçek hata)

Kullanıcının teşhisi doğruydu: "sadece ana rapor üzerinde onaylıyor, diğer rapordan seçince iptal ediyor."

- `CompleteSelectionOutside`, seçim dışına tıklamayı "uygula ve bitir" hareketi sayar ve isabet testini **katlanmış** noktayla yapar. Oysa rapordan kaldırılan parça **alan koordinatlarında** duruyor; başka bir kopyaya yapılan tıklama katlandığında parçanın alan konumuyla hiç örtüşmüyor, test her seferinde "dışarıda" diyor, tıklama seçimi uygulayıp kendisini tüketiyordu. Sonuç: diğer kopyada yeni bir seçim başlatılamıyor, önceki de gidiyordu.
- Repeat görünümünde bu hareket devre dışı: kaldırılan parçayı **Enter** uyguluyor, tıklama ise yalnızca bir sonraki parçayı işaretlemeye başlıyor.
## Color Handling 3–5 ve Advanced Color Picker tekerlek girişi

- Color Handling Wizard içindeki Find adımı Exact / Minor / Differences / All benzerlik seviyelerini ve canlı eşleşme sayısını sunar.
- Add adımı hedef başlangıç indeksini, yalnız desende kullanılan renkleri, belirli indeksten eklemeyi ve yalnız boş indekslere eklemeyi destekler.
- Define Overlap Table eşlemeyi JSON olarak kaydedip geri yükleyebilir.
- Advanced Color Picker RGB, HSV, Lab ve XYZ (D65) sayısal alanlarını birlikte gösterir. Fare tekerleği alanın üstündeyken değeri değiştirir; Shift daha ince 0,1 adım kullanır.
- Insert/Remove fare akışına bağlandı; repeat seçim/pattern bırakma ve seçim modu renkleri güncellendi. Onaylanmamış seçim turuncu, Size mavi, Repeat Selection mor gösterilir.

## Insert/Remove önizleme, Clipboard belge açma ve Windows Open With

- Insert/Remove Dynamic sürüklemesinde etkilenecek yatay/dikey bant yarı saydam mavi alanla, uygulanacak satır/sütun sayısı da bandın üzerinde gösterilir.
- Fill > Scale eklendi. Insert/Remove sonucu oluşan yeni eksen uzunluğuna desen pikselleri en yakın komşu yöntemiyle streçlenir; indeksli renk yapısı korunur.
- File > Open Image from Clipboard, Windows panosundaki uyumlu bitmap'i en fazla 256 ayrı renkle yeni indeksli belge olarak açar. Komut Keyboard Shortcuts kaydına da eklendi.
- Uygulama komut satırından gelen desteklenen dosya yollarını açar. Named-pipe tabanlı tek-instance yönlendirmesi sayesinde Windows Open With ile ikinci kez çağrılırsa yeni süreç dosya yollarını açık RugCAD penceresine gönderir, pencereyi öne getirir ve kapanır.

## File Browser

- File > Browser ve varsayılan Shift+B kısayolu, Browser'ı Design workspace içinde varsayılan olarak büyütülmüş bir MDI penceresi şeklinde açar. Pencere geri alınabilir, taşınabilir, boyutlandırılabilir ve diğer tasarım pencereleriyle düzenlenebilir.
- Sol ağaç Favoriler, Pictures, Desktop, This PC diskleri ve Network girişini gösterir; klasörler açıldıkça alt dallar yüklenir. Adres çubuğu UNC/ağ yolu da kabul eder.
- Sağ taraf açık klasördeki desteklenen RugCAD ve raster dosyalarını küçük resim, piksel ölçüsü, dosya boyutu ve varsa Paper Format kalite bilgisiyle kartlar halinde gösterir. Çift tıklama dosyayı mevcut standart açma yoluyla açar.
- Browser kendi içinde birden fazla klasör sekmesi ve her sekme için geri/ileri geçmişi sunar.
- Settings > Browser sekmesi başlangıç klasörü, kart/küçük resim boyutu, Paper Format bilgisinin görünürlüğü ve satır başına bir favori klasör ayarını kalıcı saklar.
- Browser sekmesinin seçili öğesi `TabItem` olduğu halde doğrudan `BrowserTab` sanıldığı için ilk yüklemede null reference oluşuyordu; aktif sekme artık `TabItem.Tag` üzerinden çözülür. Home sol menüsüne aynı Browser penceresini açan düğme eklendi.
- Klasör ağacındaki klasörlere sağ tıklanınca Add to Favorites komutu çıkar; klasör anında favorilere eklenip Browser ayarlarına kaydedilir. Favorites kökü her yeni Browser penceresinde varsayılan olarak genişletilmiş açılır.


## Selection içindeki çizim araçları performansı

- Airbrush, Brush, Pencil, Eraser ve Clone Stamp canlı selection tamponuna yazarken artık her pikselde tüm selection önizlemesini yeniden kurmuyor. Bir araç örneğinin ürettiği pikseller aynı tamponda toplanıyor ve capture/cache bildirimi örnek başına yalnızca bir kez yapılıyor.
- Selection maskesi, coverage, repeat koordinat katlama, undo kaydı ve uygulama sonucu değiştirilmedi. Optimizasyon yalnızca önizleme bildirimlerinin sıklığını azaltır; repeat selection davranışı aynı kalır.
- Belge üzerinde selection olmadan çizimde mevcut kısmi bitmap güncellemesi korunur. Line, rectangle, ellipse, polygon, curve, gradient, bucket ve text zaten tek toplu komutla çalıştığı için sonuçlarını veya raster kurallarını değiştirecek ek bir optimizasyon uygulanmadı.
- Kontrast cursor 24 piksele indirildi; artı cursorunun sağ-alt siyah çizgisi 1 piksel ofset kullanır.

## Airbrush performansı ve ortak çizim stilleri

- Selection dışında çalışan Airbrush artık her zamanlayıcı tick'inde ayrı `PaintStrokeCommand` ve tam belge yenilemesi üretmez. Basılı tutma süresince pikseller mevcut canlı stroke'a eklenir, yalnızca değişen pikseller bildirilir ve mouse bırakıldığında tek undo kaydı oluşur.
- Airbrush püskürtme miktarı Tool Options içindeki Density ile yönetilir. Ayrı tick ayarı aynı kullanıcı amacını tekrar ettiği için kaldırıldı; zamanlayıcı yalnızca dahili çizim örnekleme ayrıntısıdır.
- Tool Options'ın ortak Drawing style seçimi `SingleColor`, `Multicolor`, `Scatter` ve `Pattern` seçeneklerini piksel üreten çizim araçlarına sunar. Multicolor panelindeki renkler sırayla veya rastgele kullanılır; Pattern doğrudan seçili Active Pattern'i örnekler.
- `Repeat pattern` ayarı araçlara dağılmış kontrollerden kaldırılıp Active Patterns paneline taşındı. Açıkken motif döşenir; kapalıyken her çizim işlemi bir motif kullanır. Ayar oturumlar arasında saklanır.
- Airbrush'ın Pattern stilinde motif maskesi dışındaki noktalar artık ön plan rengine düşmez; o noktalar boyanmadan bırakılır.
- Airbrush Scatter, Multicolor listesinden rastgele renk seçmek yerine mevcut desenin püskürtme çevresindeki piksellerini örnekleyerek karıştırır.
- Active Patterns panelindeki küçük 3×3 ok düğmeleri motif başlangıcını dokuz noktaya (köşeler, kenar ortaları ve merkez) hizalar. Varsayılan sol üsttür. Repeat açıkken döşeme fazını, kapalıyken tek motifin tasarımdaki yerini belirler ve kalıcı saklanır.
- Bucket kendi Fill stilini kullandığı için ortak Drawing style alanı Bucket seçiliyken gizlenir; Tool Options içinde iki boyama tipi görünmez.
- Ortak Scatter stili de mevcut desen piksellerini aracın çevresinden örnekler. Pencil, Brush, çizgi ve şekil gibi ortak çizim yolunu kullanan araçlar artık Multicolor listesinden rastgele renk seçmek yerine desen dokusunu karıştırır.

## Bucket Unprotected Colors sınırı

- `Unprotected Colors` artık belgedeki bütün stop olmayan pikselleri topluca seçmez. Flood-fill tıklanan pikselden başlar, normal renk sınırlarını geçebilir ancak Stop olarak işaretli bir pikseli seçmez veya aşmaz.
- Stop renkli kapalı bir konturun içindeki noktaya tıklanınca yalnızca o konturun iç bölgesi etkilenir. Doğrudan Stop renk üzerine tıklanırsa işlem yapılmaz.

## Active Patterns kısa arayüz ve alt yardım

- Active Patterns panelindeki uzun açıklama paragrafı kaldırıldı; uzun işlem adları kısaltıldı. Panel, düğme veya motif listesinin üzerine gelindiğinde açıklama Main Form alt yardım alanında gösterilir.
- Alt yardım alanında geçici bir araç açıklaması bulunmadığında `Yardım kitapçığı için F1'e basın.` varsayılan mesajı görünür.

## Ctrl+V fare merkezli yapıştırma

- Ctrl+V ile yapıştırılan parçanın merkezi, aktif tasarım üzerinde farenin son bulunduğu tasarım pikseline gelir. Normal görünümde parça tuval dışına taşmayacak şekilde kenara yaslanır; repeat görünümünde rapor kopyası üzerindeki alan koordinatı korunur.
- Fare henüz aktif tasarımın üzerine hiç girmediyse güvenli geri dönüş olarak sol üst başlangıç kullanılır.
- Yapıştırılan içerik doğrudan onaylanmış floating pattern selection olarak açılır. Piksel capture selection ile birlikte hareket eder; boş bir maske geride kalmaz. Enter veya seçimi uygulama işlemi içeriği belgeye tek undo adımıyla işler.
- Ctrl+V tamamlanınca Size modu otomatik açılır ve sekiz mavi ölçek tutamacı görünür; kullanıcı parçayı uygulamadan önce taşıyabilir veya boyutlandırabilir.
- Yapıştırılmış seçim belgeye uygulandıktan sonra geçmiş üç aşama tutar: ilk Undo onaylanmış floating seçimi ve Size tutamaklarını geri getirir; ikinci Undo belgeye yazılmış pikselleri kaldırırken seçimi görünür bırakır; üçüncü Undo yapıştırılmış seçimi kaldırır. Redo aynı aşamaları ileri yönde uygular.
- Canvas selection yenilendiğinde onaylanmış bir floating capture görürse mavi Size modunu yeniden kurar. Bu kural undo/redo olay sırasından bağımsızdır; geri dönen yapıştırma seçimi daima sekiz ölçek tutamacıyla görünür.

## Color Handling Wizard klavye kullanımı

- Beş ana renk işleme seçeneğinin tamamı arayüzde 1–5 olarak numaralandırılmıştır. Bir sayısal metin alanı düzenlenmiyorken üst sıradaki `1`, `2`, `3`, `4` veya `5` tuşu ilgili seçeneği etkinleştirir.
- Enter, seçili renk işleme yöntemiyle Finish işlemini uygular.
- Wizard açıldığında ilk ana seçenek otomatik odaklanır; yukarı/aşağı ok tuşları ana seçenekler arasında dolaşır.

## Numpad ile seçim kaydırma

- Num Lock kapalı numpad tuşları ve Num Lock açıkken `Shift+numpad`, onaylanmış seçimi sekiz yönde bir piksel kaydırır; merkez 5 hareket üretmez. Ctrl eklendiğinde adım beş pikseldir.
- WPF'nin numpad tuşlarını `End/Down/PageDown/Left/Clear/Right/Home/Up/PageUp` olarak çevirdiği klavyeler için aynı yön eşlemesi ana pencere girişinde de uygulanır. Canvas içindeki alt kontrollerde kalan klavye odağı hareketi engellemez.
- Windows, Num Lock açıkken Shift+numpad basışında Shift'i modifier listesinden geçici olarak çıkarabildiği için fiziksel sol/sağ Shift durumu ayrıca okunur. Böylece olay pan kısayoluna düşmeden seçim kaydırma tarafından tüketilir.

## Stamp performansı

- Stamp sürüklemesinde iki mouse örneği arasındaki ara merkezler artık ayrı ayrı büyük fırça taraması ve ekran bildirimi üretmez. Süpürülen fırça izleri tek piksel kümesinde birleştirilir ve her mouse olayı için bir kısmi bitmap güncellemesi yapılır.
- Clone Stamp aynı hedef pikseli bir örnekte yalnızca bir kez işler. Active Pattern Stamp, Brush ve fırça tipi Eraser da aynı toplu sürekli-çizim yolunu kullanır.
- Hedefte zaten aynı palet indeksi bulunan pikseller canlı güncelleme listesine eklenmez; undo, stop/park, selection ve repeat katlama davranışları korunur.
### Yapıştırılmış selection için Undo sırası

Desene işlenmiş bir yapıştırma geri alınırken aşamalar korunur: ilk Undo parçayı işlendiği konumda onaylanmış floating selection olarak ve mavi Size tutamaçlarıyla geri getirir. İkinci Undo yalnızca desene yazılmış pikselleri geri alır; floating selection ile Size tutamaçları kalır. Arada taşıma veya ölçekleme yapıldıysa bunlar önce geri alınır. Bu geçici değişiklikler kalmadığında üçüncü Undo selection'ı kaldırır.

Ctrl+V ile oluşan parçanın yapıştırma kimliği artık geçici Canvas durumundan bağımsız olarak floating selection üzerinde taşınır. Parça taşınsa veya ölçeklense bile commit geçmişi son konumu ve son capture'ı kaydeder; Undo kaynak selection koordinatlarına dönmez.

Selection taşıma işlemleri mouse-up veya tek klavye adımı başına ayrı bir floating-history kaydı üretir. Yapıştırma oturumu ayrıca bağımsız bir transaction ile izlenir; Canvas araç durumu değişse bile commit sıradan baked-selection geçmişine düşmez. Undo ile geri gelen onaylanmış parçada Size isteği `ContextIdle` aşamasında zorunlu uygulanır.

Tool Options içindeki ortak `Drawing style` yalnızca ortak renk kaynağıyla gerçekten piksel çizen araçlarda gösterilir. Stamp, Gradient, Color Gradient ve Bucket kendi kaynak/dolgu davranışlarını kullandığı için; seçim, gezinme, taşıma ve Insert/Remove araçları da renk çizmediği için bu bölüm onlarda gizlidir.

Onaylanmamış selection yalnızca turuncu alan çerçevesi ve tutamaçlarını çizer. Enter öncesinde Park filtreli kontur, seçim tint bitmap'i ve Stop renk üst katmanı üretilmez. Bucket'ın ayrı Fill seçicisi kaldırılmıştır; Single Color, Multicolor, Scatter ve Pattern ortak Drawing Style grubundan yönetilir.

Eski Active Patterns paneli `Patterns` deposu olmuştur; listedeki seçim aktif motifi kendiliğinden değiştirmez. `Use`, gerekirse Color Handling Wizard sonucuyla hedef desen paletine eşlenmiş geçici motifi yeni `Active Pattern` paneline aktarır. Active Pattern yalnızca aktif motif önizlemesini, repeat seçeneğini ve 3×3 başlangıç yönünü gösterir. Kısayol ekranındaki menü yolları WPF erişim tuşu alt çizgilerinden arındırılır ve araç çubuğu düğmeleri ana çubuğun yazı tipi, boyutu, boşluğu ve ikon içeriğini korur.

Onaylanmamış selection taşınırken ağır içerik önizlemesi yerine turuncu hedef çerçevesi hareket eder. Alt durum çubuğu floating selection üzerindeki gerçek çalışma pikselini gösterir ve selection bilgisine başlangıç X/Y ile maskenin gerçek piksel sayısını ekler. Eski kayıtlı AvalonDock düzenleri yüklenirken `Active Patterns` başlığı `Patterns` olarak güncellenir ve eksik `Active Pattern` paneli yerleşime otomatik eklenir.

### Selection preview and pattern panel follow-up (2026-09-20)

- An unconfirmed selection now shows a lightweight orange target rectangle while it is moved; Park/Stop filtering is still deferred until confirmation.
- Confirmed selections fall back to the contour display while Park or Stop colors are active, preventing the initial tint frame from looking palette-corrupted before the first move.
- The status bar samples the composited working pixel, so hovering a floating selection reports the selection pixel rather than the design underneath.
- Selection status includes its start X/Y, dimensions, and effective selected-pixel count.
- Restored layouts migrate the former `Active Patterns` title to `Patterns` and create the separate `Active Pattern` panel when it is absent. The active panel also resubscribes when reopened.
- Selection menus now use `Add to Patterns`; `Set to Active Pattern` assigns the current selection directly without adding it to the saved depot.
- Selection confirmation is part of selection history. Committing a moved/scaled selection records a separate live-selection stage, preserves its movement edits, and restores the floating selection at its final coordinates before the document pixels are undone.
- Active Pattern is intentionally compact: image preview, Repeat checkbox, and a Drawing Style-like combo box for the nine pattern-start anchors.
- Bucket has its own `Spray` drawing style (with its spray options), while other tools keep `Scatter`. Patterns can be dropped from the Patterns depot onto Active Pattern. Activating a motif no longer opens Color Handling; palette handling is deferred until the motif is actually applied to a design.
- Choose Color now gives every RGB, HSV, Lab, and XYZ channel a mouse-wheel-enabled gradient slider whose track previews that channel's reachable colors.
- The dockable Palettes panel stores named palettes under the RugCAD application-data folder. It supports search, selected-palette swatch preview, saving the current palette, applying it undoably, updating its name/colors from the current design, and deletion. Saved layouts from older versions receive the new panel automatically.
- Patterns no longer opens an item on double-click. Its context menu contains Edit, Open in New Window, and Set Active Pattern; dropping onto the Active Pattern preview consumes the drop there instead of opening a design.
- Palettes now keeps the dock panel to search/list plus Add Palette, Update, and Delete. Add and Update open a dedicated palette editor prefilled from the current or selected palette; the editor shows the preview and opens Choose Color when a swatch is double-clicked.
- Pattern and palette lists now expose their edit/delete/activate actions from right-click menus. The palette creation button is `New`. Active Pattern consumes drops across its whole panel, with a MainWindow fallback that prevents those drops from opening a new design. Choose Color coalesces rapid slider preview renders to reduce UI stalls.
- Palettes keeps only a `New Palette` button below the list; Edit and Delete remain in the context menu. The palette editor's `Use Current Palette` button replaces its working colors with the palette of the currently selected design.
- Removing a saved Pattern or Palette asks for confirmation, names the affected item, and defaults to `No` so an accidental Enter cannot delete library data.
- Palettes has an `Apply This Palette` context command; differing palettes go through Color Handling. The wizard remembers its last mode, similarity level, add method, and index inputs. Top-row and numpad 1–5 choose wizard modes; Up/Down moves through the currently visible radio group; Enter finishes and saves the choices.

- Palettes panelinde liste altındaki Apply Palette düğmesi seçili kayıtlı paleti aktif desene uygular; farklı paletlerde aynı Color Handling akışı kullanılır. New Palette düğmesi bunun altında kalır.
- Patterns panelinin alt eylem alanında yalnızca Use bulunur; silme işlemi sağ tık menüsündedir ve onay istemeye devam eder.
- Patterns > Edit ile açılan motif sekmesi kütüphane kaydıyla ilişkilidir. Normal Save/Ctrl+S artık Save As penceresi açmadan aynı motif kaydını, önizlemesini ve aktif motif referansını günceller. Open in New Window bağımsız tasarım açmayı sürdürür; açıkça Save As seçilirse dosyaya kaydetme yolu kullanılabilir.

### Line direction controls (2026-09-20)

- Line aracı Free modundayken Shift, çizgiyi en yakın yatay, dikey veya 45 derece doğrultuya kilitler. Önizleme ve mouse-up sonucu aynı kısıtlanmış uç noktayı kullanır.
- Tool Options içindeki Line bölümünde Free, Horizontal, Vertical ve Angled yönleri bulunur. Angled değeri artırma/azaltma düğmeli sayısal alandan 1–89 derece arasında ayarlanır ve varsayılanı 45 derecedir.
- Açı hesabı Warp/Weft yoğunluklarını kullanır; bu nedenle 45 derece, piksel hücreleri kare olmadığında da fiziksel desende 45 dereceyi korur.
- Line Tool Options içindeki Pixel X ve Pixel Y değerleri çizginin yatay/dikey kalınlığını 1–20 piksel arasında bağımsız ayarlar. Kalınlık, Pixel Cord ve yön/açı kısıtlamasından sonra oluşan çizgiye uygulanır.
- Keyboard Shortcuts penceresi yalnızca ana formun gerçek ToolBarTray sırasını kopyalar. Standart komut ikonları, Repeat ikonları ve Drawing Tools ikonları ana menü/toolbar görünümüyle eşleşir. Daha önce eşleşmediği için pasif kalan Home, Paste Special, Graphics Repeat, About, dosya, zoom ve transform toolbar komutları seçilebilir kısayol hedefleridir.

### Polygon selection behavior (2026-09-20)

- Polygon seçim aracı mouse basılı sürüklemesinden serbest el noktaları üretmez. Her sol tıklama yalnızca bir köşe ekler; Enter son noktayı başlangıca bağlayıp seçimi oluşturur. Backspace son noktayı kaldırır; Delete ve Escape işlemi iptal eder.
- Polygon bir seçim aracı olduğu için ortak Drawing Style bölümü onda gösterilmez.
- Polygon ve Polygonal Lasso için ayrı `Stay inside the clicked color` seçeneği vardır ve varsayılanı kapalıdır. Açıldığında poligon alanı, ilk köşenin altındaki çalışma rengine ve Magic Wand toleransına göre kırpılır; aynı davranış repeat alanında da uygulanır. Lasso'nun kendi seçeneği bağımsız kalır.


### Polygon point selection ve clipboard güncellemesi (2026-09-21)

- Polygon seçim aracı mouse basılı sürüklemesinden serbest el noktaları üretmez. Spline Through Points gibi her sol tıklama yalnızca bir köşe ekler ve bekleyen zincir açık gösterilir. Enter son köşeyi başlangıç köşesine bağlar, alanı seçime dönüştürür ve mavi Size tutamaçlarını açar.
- Enter ile oluşturulan polygon selection aynı işlem içinde onaylanır; turuncu/onay bekleyen aşamada kalmaz. Normal ve repeat görünümünde doğrudan mavi Size durumuna geçer.
- Backspace yalnızca son polygon noktasını kaldırır. Delete ve Escape, daha eski bir selection açık olsa bile önce bekleyen polygon işlemini tamamen iptal eder.
- Polygon seçiminde ortak Drawing Style gösterilmez. Polygon ve Polygonal Lasso için ayrı `Stay inside the clicked color` seçeneği vardır ve varsayılanı kapalıdır. Açıldığında alan ilk köşenin altındaki çalışma rengine ve Magic Wand toleransına göre kırpılır; repeat alanında da aynı filtre uygulanır.
- Polygon ikonu, düz segmentlerle bağlanmış belirgin köşe noktaları kullanır; serbest el Lasso aracından görsel olarak ayrılır.

#### Copy/paste ve Windows clipboard

- Copy, RugCAD içindeki mask ve feather coverage bilgisini taşıyan dahili selection capture'ını korurken Windows clipboard'a standart WPF bitmap ve gerçek 8-bit indexed `CF_DIB` yazar. Photoshop/Paint gibi true-color uygulamalar bitmap'i, Texcelle gibi indexed-color uygulamalar ise piksel indeksleriyle RGB renk tablosunu okuyabilir.
- 8-bit DIB satırları DWORD hizalı ve bottom-up yazılır. DIB biçiminde selection alpha katmanı bulunmadığı için dış uygulamaya giden dikdörtgen dışı veya çok düşük coverage alanları mevcut arka plan palet indeksiyle düzleştirilir. RugCAD'den RugCAD'e paste dahili capture'ı kullandığı için gerçek mask ve coverage kaybolmaz.
- Clipboard verisine RugCAD kaynak işareti eklenir. Ctrl+V bu işareti görürse sistem bitmap'ini yeniden dönüştürmek yerine palette, mask ve coverage bilgisi tam olan dahili kopyayı kullanır.
- `CanPaste`, dahili clipboard yanında Windows clipboard'daki DIB veya bitmap içeriğini de kabul eder. Clipboard başka bir süreç tarafından kısa süreli kilitlenirse hata kullanıcı akışını bozmaz.
- Dış kaynaktan gelen 8-bit indexed DIB'in boyutu, indeks matrisi, renk tablosu ve gerçekten kullanılan palet indeksleri okunur. Kaynak palet hedef desenle uyuşmadığında Color Handling Wizard devreye girer. Cancel, eski bir RugCAD kopyasına geri düşmeden paste işlemini durdurur; kullanılmayan palet hücrelerindeki farklar gereksiz eşleme sebebi sayılmaz.
- Indexed olmayan Windows bitmap'leri hedef desenin en yakın palet renklerine dönüştürülür. Tüm alpha baytları sıfır olan 32-bit clipboard DIB'leri saydam kabul edilmez; Windows veya Photoshop'un padding olarak bıraktığı bu baytlar nedeniyle boş selection oluşması engellenir. Gerçek kısmi alpha coverage olarak korunur.
- Ctrl+V parçayı son mouse konumuna ortalar; normal görünümde belge sınırlarına yaslar, repeat alanında sanal alan koordinatını korur. Sonuç onaylanmış floating selection ve mavi Size tutamaçlarıyla gelir.
- Paste geçmişi capture'ın son konumunu ve son ölçeğini saklar. Commit sonrasında Undo önce aynı konumdaki floating selection'ı ve Size tutamaçlarını geri getirir, sonraki adım belgeye yazılan pikselleri kaldırır, üçüncü adım selection'ı kaldırır. Redo aynı aşamaları ileri yönde kurar.

### Magic Wand ve Area Selection canlı tint önizlemesi (2026-09-21)

- Magic Wand ve Area Selection tek tıklamada maskeyi oluşturduğu için, seçim henüz Enter ile onaylanmamış olsa da maske anında görünür.
- Görünüm, kullanıcının seçtiği Selection Display ayarını izler: Black Tint siyah, White Tint beyaz yarı saydam örtü kullanır; Marching Ants seçiliyse gerçek maske konturu gösterilir.
- Bu istisna yalnızca Magic Wand ve Area Selection içindir. Park/Stop filtreleme ve Stop piksellerini üstte yeniden çizme işlemleri onay öncesinde çalıştırılmaz; diğer seçim araçlarının hafif önizleme davranışı korunur.
### Magic Wand ve Area Selection bağlantı tipi (2026-09-21)

- Her iki aracın Tool Options bölümüne Bucket ile aynı `Normal` ve `Diagonal` tipi eklendi.
- Normal, yalnızca yatay/dikey dört komşuyu bağlı kabul eder. Diagonal, dört çapraz yönü de ekleyerek sekiz komşulu seçim oluşturur.
- Magic Wand ayarı yalnızca Magic Wand seçimini etkiler; aynı maske yardımcısını kullanan Gradient gibi araçların bağlantı davranışını değiştirmez. Area Selection kendi bağımsız bağlantı ayarını kullanır.
### Global selection tint araç seçenekleri ve kısayolu (2026-09-21)

- Magic Wand ve Area Selection Tool Options bölümlerine Black Tint / White Tint seçimi eklendi. İki bölüm aynı global Selection Display ayarını kullanır; birinden yapılan değişiklik tüm açık tasarımlara, diğer araç paneline ve Select menüsüne yansır.
- `Select > Selection Display > Toggle Black / White Tint` komutu menüye ve Keyboard Shortcuts listesine eklendi. Varsayılan `Ctrl+Shift+H` her basışta siyah ve beyaz tint arasında geçer; kullanıcı kısayol ekranından başka bir tuş atayabilir.
- Marching Ants menüden seçilmeye devam eder. Toggle komutu Marching Ants durumunda ilk olarak Black Tint'e geçer.
### Settings: System ve User Data (2026-09-21)

- Settings penceresi yeniden boyutlandırılabilir hale getirildi ve System sekmesi eklendi. Undo/Redo global olarak açılıp kapatılabilir; geçmiş 1–10.000 işlem ve 64–16.384 MB arasında ayrı adım/bellek bütçeleriyle sınırlandırılır. Varsayılan kapasite 1.000 işlem ve 2 GB'tır.
- CommandHistory artık sınırsız Stack tutmaz. Her kayıt için bellek tahmini saklar, iki sınırdan biri aşılınca en eski işlemleri kaldırır ve tek bir çok büyük işlemi yine de geri alınabilir bırakır. Piksel stroke'ları ve tam belge değiştiren işlemler kendi gerçek boyutlarına yakın tahmin sağlar.
- System sekmesinde çoklu RugCAD örneklerine izin verme ve panel yerleşimini oturumlar arasında hatırlama seçenekleri bulunur. Tek örnek modu açıkken Windows'tan açılan dosyaları çalışan uygulamaya iletme davranışı korunur.
- User Data sekmesi tüm yerel RugCAD profilini tek `.rugcadbackup` dosyasına aktarır: settings, klavye kısayolları, panel düzeni/durumları, Browser sekmeleri ve favorileri, yakın geçmiş, Patterns, Palettes ve diğer AppData tercihleri. Kullanıcının tasarım dosyaları pakete dahil edilmez.
- İçe aktarma kullanıcı onayı ister ve paketi bir sonraki başlangıç için hazırlar. Böylece çalışan oturum kapanırken içe aktarılan ayarları eski durumla ezemez. Başlangıçta manifest ve arşiv yolları doğrulanır; dizin dışına çıkmaya çalışan girdiler reddedilir. Bozuk bekleyen paket karantinaya alınır ve uygulamanın başlamasını engellemez.
### Seçilebilir User Data içe/dışa aktarma (2026-09-21)

- Export ve Import düğmeleri artık işlemden önce checkbox listesi açar. Kategoriler bağımsızdır: genel uygulama/sistem ayarları, klavye kısayolları, Browser ve recent geçmişi, panel layout/durumları, Patterns ve Palettes.
- Dışa aktarma yalnızca işaretli kategorileri ve kategori bilgisini manifest içine yazar. İçe aktarma penceresinde yalnızca seçilen yedekte bulunan kategoriler etkinleştirilir; kullanıcı bunların içinden daha dar bir seçim yapabilir.
- `settings.json` bütün olarak ezilmez. Seçilen kategoriye ait JSON bölümleri mevcut ayarlara özellik bazında birleştirilir. Örneğin yalnızca Keyboard Shortcuts içe aktarılırsa System, Browser, görünüm, çizim ve panel ayarları aynı kalır.
- İçe aktarım onayı seçilen kategoriler belirlendikten sonra sorulur ve bekleyen restore kaydı kategori maskesini de saklar. Sonraki açılışta yalnızca bu maske uygulanır.
### Settings ve kütüphane liste sıralaması (2026-09-21)

- Settings sekmeleri kapsamdan göreve doğru yeniden sıralandı: System, Files, Grid & Display, Browser, User Data. Settings varsayılan olarak System sekmesinde açılır; View > Grid Settings doğrudan Grid & Display sekmesini açmaya devam eder.
- Patterns paneli depo sırasını ters görünümle sunar; en son eklenen motif listenin üstünde görünür. Ekleme, kaldırma ve sürükle-bırak sonrası görünüm anında yenilenir ve seçim korunur.
- Palettes paneli adlara göre doğal alfabetik sıralama kullanır. Sayı blokları metin olarak değil sayı olarak karşılaştırıldığı için `Palette 2`, `Palette 10`dan önce gelir. Arama filtresi aynı sıralamayı korur.
### System runtime özeti ve ayrıntılı sistem raporu (2026-09-21)

- Settings > System içindeki Runtime information; Windows sürümü, işlemci modeli, mantıksal işlemci sayısı, toplam/kullanılabilir fiziksel bellek ve .NET runtime ile süreç/OS mimarisini özetler.
- `System Details…` düğmesi yeniden boyutlandırılabilir ayrı bir pencere açar. System, Processor and memory, Runtime and RugCAD, Display, Storage ve Loaded frameworks and assemblies sekmeleri bulunur.
- Ayrıntılar makine/kullanıcı, kültür ve saat dilimi, Windows ve mimari, CPU/RAM/GC, RugCAD ve CLR sürümü, süreç kimliği/başlangıcı/working set, uygulama dizini, ekran çalışma alanı, uzak oturum, hazır disklerin kapasitesi ve uygulamanın gerçekten yüklediği assembly/framework sürümlerini içerir.
- `Copy Full Report` bütün bölümleri zaman damgalı düz metin raporu olarak panoya kopyalar; teknik destek sırasında tek işlemle paylaşılabilir.
- Ayrıntılı sistem raporu Settings içinden bağımsız olarak `Help > System Details...` yolundan da açılır. Komut Keyboard Shortcuts listesine kayıtlıdır ve kullanıcı isterse kendi kısayolunu atayabilir.

### Automatic recovery (2026-09-21)

- Settings penceresindeki `Files` sekmesinin adı `Save` olarak değiştirildi. Bu sekmede otomatik kurtarmayı açıp kapatma, kurtarma klasörü ve 1–120 dakika arasındaki kayıt aralığı bulunur; varsayılan aralık 5 dakikadır.
- Otomatik kurtarma yalnızca değiştirilmiş ve açık tasarımları RugCAD'in yerel `.rugcad` biçiminde kaydeder. Her açık tasarım için tek bir kurtarma dosyası güncellenir ve dosya önce geçici konuma yazılıp atomik olarak yer değiştirilir; çökme sırasında yarım dosya oluşma riski azaltılır.
- Normal Save başarıyla tamamlandığında o tasarıma ait kurtarma kopyası silinir. Kurtarma kopyası Browser'dan açıldığında kaynak dosyanın üzerine yazılmaz; belge kaydedilmemiş tasarım gibi açılır ve Save As ister.
- File Browser araç çubuğunda `Auto Recovery` kurtarma klasörünü açar. Sol klasör ağacında da aynı konum kalıcı bir kök olarak bulunur. `Clear Recovery History` onaydan sonra tamamlanmış kurtarma dosyalarını ve çökmeden kalabilecek geçici dosyaları siler.
- Otomatik kurtarma ayarları User Data içe/dışa aktarmasındaki genel uygulama ayarlarına dahildir; kullanıcı yeni bilgisayarda aynı etkinlik, klasör ve aralık tercihlerini geri yükleyebilir.
- Patterns içindeki `Edit` komutuyla açılan motif sekmesinin başlığına ` (Edit mode)` eklenir. Böylece kütüphane kaydını güncelleyen editör sekmesi, bağımsız tasarım olarak açılan motiften açıkça ayrılır.
- Ctrl+Tab pencere değiştirici tek bir görünür desen açıkken de gösterilir. Tek kart mevcut deseni ve başlığını gösterir; Ctrl bırakıldığında aynı desen etkin kalır.
- Açık File Browser da Ctrl+Tab pencere değiştiriciye önizleme kartı olarak katılır. Browser kartı seçilip Ctrl bırakıldığında File Browser sekmesi; desen kartı seçildiğinde Designs sekmesi ve ilgili desen etkinleştirilir.
- Ctrl+Tab pencere değiştiricinin kart alanı üzerinde, Home sayfasıyla aynı vurgu rengi ve tipografiyi kullanan ortalanmış `RUGCAD` başlığı gösterilir.
- Ctrl+Tab uygulama genelinde RugCAD tarafından tüketilir. Değiştiricide gösterilecek görünür desen veya açık File Browser yoksa hiçbir pencere gösterilmez ve WPF/Windows'un varsayılan odak ya da pencere değiştiricisine geçilmez.
- Dock alanından ayrılıp bağımsız AvalonDock penceresinde yüzen bir panelde Esc, etkin paneli gizleyerek yüzen pencereyi kapatır. Aynı panel bir kenara veya panel grubuna dock edilmişse Esc paneli etkilemez; tuş tasarım aracının normal Escape davranışına bırakılır.
- Yüzen panel algısı artık odaktaki alt kontrolün Window ilişkisine değil AvalonDock'un `LayoutAnchorableFloatingWindowControl` modeline dayanır; böylece panel içindeki liste, metin alanı veya boş yüzey odaktayken de Esc çalışır. Kaydedilmiş layout içindeki yüzen araç panelleri sonraki uygulama açılışında pencere olarak oluşturulmaz, gizli başlar ve gerekirse View > Panels üzerinden geri çağrılır.
- Menülerdeki alt menü başlıkları diğer komutlarla aynı sol girintiyi kullanır: Window > Tile, Tools > Drawing Tools, View > Grid/Panels, Transform > Mirror/Rotate ve Select > Modify/Selection Display için ayırt edici ikonlar eklendi.
- File > Save All her benzersiz açık tasarımı bir kez kaydeder; kaydetme yolu olmayan belgelerde Save As ister ve iptalde sırayı durdurur. File > Close All mevcut kaydetme onaylarını kullanarak tüm desen pencerelerini ve File Browser belgesini kapatır. İki komut da Keyboard Shortcuts ekranında atanabilir.

### Acil hata düzeltmeleri: Save As, Swap Color, Duplicate, Bucket Spray ve sürücü adları (2026-09-21)

- Save As artık hedef dosyanın uzantısız adını desen adı olarak benimser; `FilePath`, dosya türü, MDI/ana pencere başlığı, durum metni ve Recent kaydı yeni hedefe geçer. `.rugcad` ve görsel metadata'sına eski kaynak adı değil yeni dosya adı yazılır.
- Paint grubuna `Swap Color` aracı eklendi. Tıklanan pikselin palet indeksini foreground indeksiyle tüm uygun piksellerde tek undo adımı olarak değiştirir; aktif selection ve Stop korumasını izler.
- Kullanıcı tarafından atanan Duplicate Selection kısayolu panel odağında da çalışır. Edit ve Select menülerindeki iki Duplicate Selection girişi aynı `edit.duplicate` atamasını ve güncel kısayol etiketini paylaşır.
- Bucket'ın Drawing Style = Spray modu artık mevcut desen rengini yeniden örneklemez. Normal kullanım foreground'u (sağ tıkta second/background), Multicolor seçeneği açıkken Multicolor panelindeki renkleri uygular.
- File Browser `This PC` ağacındaki sürücüler Explorer biçiminde volume label ve sürücü harfiyle (`Etiket (C:)`), etiketsiz sürücüler `Local Disk (C:)` olarak gösterilir.
- Duplicate Selection mavi/onaylanmış seçimde ek bir Enter beklemeden seçimi aynı konumda taşınabilir kopya olarak kaldırır; ilk sürükle-bırak duplicate piksellerini doğrudan işler. Mavi Size tutamaçları korunur.
- Sürücü etiketi yalnızca `DriveInfo.VolumeLabel` üzerinden üretilmez. Yerel sürücüler Windows Shell görünen adını, eşlenmiş ağ sürücüleri `WNetGetConnection` ile paylaşım ve sunucu adını kullanır; örneğin `Desen_Ihracat (\\SOYDESSER) (Y:)`.

### File Browser ve genel UI performans regresyonu (2026-09-22)

- Tablet/kalem, Pencil, Zoom ve Pan için önceki incremental render optimizasyonları korunmuştur. Bu performans paketinde `OpenFileAsNewTab`, MDI akışı ve çizim gesture davranışı değiştirilmemiştir.
- File Browser kalıcı overlay olduğu için yalnızca `Visibility=Collapsed` yapmak kontrolü visual tree'den ayırmıyordu. Önceki thumbnail worker'ı ve devam eden klasör taramaları Browser kapatıldıktan sonra da CPU/disk/GC kullanabiliyordu. Browser görünmez olduğunda klasör taraması, seçili dosya preview'su ve grid thumbnail kuyruğu artık iptal edilir; gizliyken gelen navigation yalnızca hedef yolu kaydeder ve gerçek I/O Browser yeniden görünür olduğunda tek kez başlar.
- Oturum geri yüklemede her kayıtlı Browser sekmesi `OpenTab` sırasında yükleniyor, seçili sekme değişimi de aynı klasörü ikinci kez tarayabiliyordu. Sekmeler artık lazy-load çalışır: başlangıçta yalnızca aktif sekme yüklenir, diğer sekmeler ilk kez seçildiklerinde taranır. Boş klasör ile henüz yüklenmemiş sekmeyi ayırmak için `BrowserTab.HasLoadedContents` kullanılır.
- Klasör isimlerinin enumeration'ı background thread'de olsa da `BrowserFile.CreateLightweight/CreateFolder` içindeki size/modified metadata erişimleri UI thread'de yeniden yapılıyordu. Ana klasör yükleme yolu artık `DirectoryInfo.EnumerateFileSystemInfos()` ile tek background geçişte görünürlük, boyut ve tarih metadata'sını toplar; UI thread yalnızca tek collection reset yapar. Eski/yeni yükler cancellation token ile birbirini iptal eder.
- File Browser sol ağacı açılırken mapped drive/shell adları ve recovery klasörü oluşturma işi UI thread'i bekletebiliyordu. Sürücü keşfi ve recovery klasörü hazırlığı background'a taşındı. Alt klasör expansion'ı `Directory.Exists` dahil tamamen async çalışır ve en fazla 500 child tek tek `ObservableCollection.Add` etmek yerine tek `Reset` bildirimiyle güncellenir. TreeView virtualization/recycling de açılmıştır.
- Grid/List ortak item style'ındaki `Loaded` olayı List görünümünde bile görünmeyen thumbnail'ları decode ediyordu; Grid kartında ayrıca ikinci kez tetikleniyordu. Thumbnail başlatma artık yalnız Grid kartının `Loaded/DataContextChanged` olayındadır. Görünür kartlar yüksek önceliklidir ve background look-ahead 64 dosyadan 8 dosyaya düşürülmüştür.
- `.rugcad` preview üretiminde yalnız width 320px ile sınırlandığı için dar/çok uzun tasarımlar binlerce satırlık bitmap oluşturabiliyordu. Preview her iki eksende en fazla 320px olacak şekilde sınırlandırıldı. RUGCAD preview rasterizasyonu piksel başına `SKBitmap.SetPixel` yerine tek managed BGRA buffer + `Marshal.Copy` kullanır. Raster image tarafında `SKCodec.GetScaledDimensions/GetPixels` ile mümkün olduğunda decode hedef boyuta yakın yapılır; format bunu desteklemiyorsa güvenli fallback kullanılır.
- Browser'da gezildikçe decode edilmiş thumbnail'lar sekme item'larında sınırsız tutuluyordu. Kalıcı Browser sekmelerinde bu yüzlerce MB native/managed bitmap belleğine ve GC baskısına dönüşebiliyordu. Global retained preview havuzu 128 thumbnail ile sınırlandı; eski thumbnail serbest bırakıldığında kart yeniden görünür olursa lazy decode edilir.
- Custom `VirtualizingWrapPanel` için gerçek container recycling denenmişti; ancak bu panelin mevcut `GeneratorPositionFromIndex/StartAt` gerçekleştirme hesabı recycle edilmiş generator slotlarını yeniden eşleyemediği için scroll sonrasında Grid'in tüm görünür kartları boş kalabiliyordu. Bu regresyon kullanıcı testinde doğrudan yakalandı. Panel güvenli ve daha önce çalışan `ItemContainerGenerator.Remove` yoluna geri döndürüldü; yalnız viewport dışındaki kartlar kaldırılmaya devam ettiği için virtualization korunuyor. File Browser'ın asıl performans kazanımları olan async metadata, bounded thumbnail cache, hidden-overlay cancellation ve grid-only preview decode değişiklikleri korunmuştur.
- Paper format gösterimi kapalıysa image preview sırasında embedded metadata ikinci kez okunmaz. Böylece özellikle ağ klasörlerinde gereksiz ek dosya açma/okuma azaltılmıştır.
- File Browser dışındaki genel takılma için ayrı bir bellek kaçağı giderildi: `DesignCanvas` statik `CanvasViewOptions.ShowScrollbarsChanged`, `CanvasViewOptions.SelectionDisplayChanged` ve `GridPreferences.Changed` event'lerine constructor'da abone olup kapanırken çıkmıyordu. Event'ler artık `Loaded` sırasında idempotent bağlanır ve `Unloaded` sırasında sökülür; kapatılan tasarım canvas'ları statik event referansıyla bellekte tutulmaz.
- Regression testine aşırı uzun raster görsel için thumbnail'ın iki eksende de 320px altında kalması ve thumbnail eviction sonrası `BrowserFile` nesnesinin yeniden lazy-load edilebilir duruma dönmesi kontrolleri eklendi.
- Manuel performans kontrolü için özellikle şu senaryolar kullanılmalıdır: yüzlerce/binlerce BMP/RUGCAD içeren yerel klasör; offline veya yavaş mapped/UNC klasör; 5+ kayıtlı Browser sekmesiyle uygulama açılışı; Browser Grid'de hızlı scroll; Browser kapatıldıktan hemen sonra tablet Pencil/Zoom/Pan; çok sayıda tasarım sekmesini açıp kapattıktan sonra uzun çizim oturumu.

### Pan sınırı ve kısmi canvas dışı hareket (2026-09-22)

- Pan sürüklemesi eskiden mouse-down anındaki sabit başlangıç noktasından toplam delta hesaplıyordu. `ClampPan()` sınır dışı offset'i geri çekse bile pan başlangıcı eski kaldığı için sonraki mouse/tablet paketi aynı sınır dışı değeri yeniden üretiyor ve desen canvas kenarında sürekli ileri-geri zıplıyordu.
- Pan artık ardışık input paketleri arasındaki incremental delta ile çalışır. Her örnekte yalnız son pointer konumundan fark eklenir, pointer referansı hemen güncellenir ve `ClampPan()` çizimden önce uygulanır. Scrollbar chrome güncellemesi performans için yaklaşık 16 ms cadence ile throttled kalır.
- Finite/bounded tasarım görünümündeki eski sınır, deseni viewport içine tamamen hapsediyordu; viewport'tan küçük desenler ayrıca zorla ortalanıyordu. Yeni davranışta desen kısmen canvas dışına taşınabilir ve küçük desenler de serbestçe pan edilebilir.
- Tek hard limit, desenin tamamen kaybolmamasıdır. X ve Y eksenlerinde en az 1 ekran pikseli görünür kalacak şekilde pan offset sınırları hesaplanır. Bu sınır zoom seviyesinden ve design-pixel aspect oranından bağımsızdır.
- Mouse/tablet drag-pan, wheel/keyboard pan, zoom sonrası clamp ve WPF scrollbar `Minimum/Maximum/Value` hesapları aynı `GetFinitePanBounds` sonucunu kullanır; böylece scrollbar ile canvas offset'i birbirini farklı sınırlara çekmez.
- Unbounded repeat görünümünün sonsuz alan/wrap davranışı değiştirilmemiştir; bu yeni kısmi-dışarı-pan sınırı yalnız finite/bounded içerik için geçerlidir.

### View: tüm desenleri Fit to Window ve daha uzak Zoom Out (2026-09-22)

- `View > Fit All Designs to Window` eklendi. Komut `Workspace.Children2` içindeki bütün açık `DesignCanvas` örneklerini dolaşır ve mevcut tek-doküman `FitToWindow()` akışını yeniden kullanır; aktif tasarım değiştirilmez.
- Komut Keyboard Shortcuts sistemine `view.fitAllWindows` kimliğiyle kaydedildi; kullanıcı isterse kendi kısayolunu atayabilir.
- Minimize edilmiş veya henüz ölçülmemiş bir MDI canvas'ın viewport boyutu 0 ise `FitToWindow()` isteği kaybolmaz; `_needsInitialFit` üzerinden bir sonraki geçerli SizeChanged ölçümünde uygulanır.
- Finite/bounded tasarımlarda normal minimum zoom tabanı %25'ten %20'ye indirildi. Büyük tasarımlarda dinamik minimum artık `fitZoom * 0.9` yerine `fitZoom * 0.8` kullanır; böylece Fit to Window seviyesinin yaklaşık %20 altına kadar biraz daha uzaklaşılabilir.
- Unbounded repeat görünümünün mevcut %2 / `fitZoom * 0.9` davranışı değiştirilmedi; sonsuz repeat zaten çok daha uzak zoom-out'a izin veriyordu.

### Design Comparison: farklı ebatlarda normalize Pan/Zoom (2026-09-22)

- Native size karşılaştırmasındaki eski side-by-side senkron, `HorizontalOffset / ScrollableWidth` ve `VerticalOffset / ScrollableHeight` oranlarını karşı tarafa uyguluyordu. Bu yalnız iki görüntü de aynı eksende viewport'tan büyükken anlamlıydı. Örneğin 960×1500 ve 321×600 desen aynı zoom seviyesinde karşılaştırıldığında küçük desen yatayda viewport'a sığıyor ve `ScrollableWidth=0` olduğu için yatay senkron tamamen kaybolabiliyordu.
- Her comparison viewer artık görüntünün çevresinde viewport kadar nötr pan alanı sağlayan ayrı bir pan surface kullanır. Böylece görüntü viewport'tan küçük olsa bile X/Y pan edilebilir; görüntünün sol/sağ ve üst/alt kenarı viewport merkezine kadar getirilebilir.
- Side-by-side senkron artık scrollbar oranı kullanmaz. Kaynak viewport merkezinin görüntü içindeki normalize konumu `0..1` aralığında hesaplanır ve hedef görüntünün kendi render boyutu + kendi viewport ölçüsü üzerinden hedef offset'e dönüştürülür. Ebat ve aspect oranı ne kadar farklı olursa olsun aynı göreli bölge takip edilir.
- Mouse-wheel zoom anchor hesabı pan surface içindeki gerçek image origin'ini dikkate alır. Böylece zoom sırasında cursor altındaki görüntü noktası, görüntülerden biri viewport'tan küçük olsa dahi yerinde kalır.
- Zoom slider / programatik zoom, wheel anchor yoksa mevcut normalize görüntü merkezini korur. İlk açılış, size-mode değişimi ve Fit komutu ise bilinçli olarak `(0.5, 0.5)` merkeze döner.
- Viewer viewport ölçüsü scrollbar/pencere resize nedeniyle değiştiğinde pan surface boyutları yeniden hesaplanır ve normalize odak korunur. Overlay ve Difference viewer'ları da aynı pan yüzeyi altyapısını kullanır.
- Pan drag başlangıç noktasına göre toplam delta biriktirmek yerine input paketleri arasında incremental delta kullanır. Sınırda gizli overscroll birikmesi ve ters yöne dönerken gecikme engellenir.
- Senkron hedef offset mevcut değere 0.25 px'den yakınsa yeni `ScrollTo...` çağrısı yapılmaz; pan sırasında gereksiz `ScrollChanged` ve layout trafiği azaltılır.

### Pan sınırı: serbest taşıma yalnız Pan aracı ve Alt+Space (2026-09-22)

- Finite/bounded Design Workspace için pan sınırı iki moda ayrıldı: `Strict` ve `PartialOffCanvas`.
- `PartialOffCanvas` yalnız açık Pan aracıyla sol-mouse sürüklemede ve geçici `Alt+Space + sol-mouse` pan gesture'ında kullanılır. Bu iki akışta desen kısmen canvas dışına taşınabilir; yalnız tamamen kaybolması engellenir.
- Mouse wheel dikey pan, Shift+wheel yatay pan, keyboard/arrow pan kısayolları, WPF scrollbar sürükleme ve zoom/layout kaynaklı offset güncellemeleri varsayılan `Strict` sınıra döndürüldü. Büyük desenlerde boş alan kenardan içeri alınamaz; viewport'tan küçük desen ilgili eksende merkezde kalır.
- Orta mouse ile pan, seçili Pan aracı olmadığı için `Strict` davranır.
- Serbest pan bittikten sonra görüntü bulunduğu kısmi-dışarı konumda kalır. Sonraki wheel/keyboard/scrollbar/zoom işlemi strict sınırı yeniden uygular.
- Canlı Pan gesture'ındaki incremental delta ve edge-bounce düzeltmesi korunur; yalnız clamp bounds aktif gesture türüne göre seçilir.

### Windows taskbar ikonunda kırmızı kare düzeltmesi (2026-09-22)

- `MainWindow.xaml` içindeki `Icon="/Assets/rugcad.png"` kaldırıldı. WPF Window.Icon override'ı, EXE'nin şeffaf Windows icon resource'unu taskbar'da kullanmasını engelliyordu.
- Uygulamanın gerçek `ApplicationIcon` kaynağı `Assets/rugcad.ico` olarak korunur. 32×32 ICO frame alpha kanalı kontrol edildi; arka plan şeffaftır.
- Sparse/MSIX identity tarafındaki `Assets/Shell/Square44x44Logo.png` da alpha kanallı/şeffaftır; targetsize `altform-unplated` asset seti ve `resources.pri` üretimi korunmuştur.
- `Assets/rugcad.png` silinmedi; About ekranındaki büyük marka görseli olarak kullanılmaya devam eder. Yalnız Windows pencere/taskbar ikonu kaynağı olmaktan çıkarıldı.



### RugScale — motif/topoloji korumalı indexed resize motoru (2026-09-22)

- Design > Resize içindeki Scale Image seçeneklerine RugCAD'e özgü **RugScale — motif & topology preservation** modu eklendi. İsim EFAB Smart Resize'dan bilinçli olarak ayrıdır; uygulama bağımsız RugCAD algoritmasıdır.
- `RugCAD.Core/Drawing/RugScale/RugScaleEngine.cs` ayrı motor olarak eklendi; mevcut `DesignResizer.Scale(...)` ve tek resize = tek Undo/Redo sözleşmesi bozulmadı. `ScaleMode.RugScale` yalnız mevcut akışa yeni bir strateji olarak bağlanır.
- RugScale RGB interpolation yapmaz. Tasarım palette-index/categorical raster olarak değerlendirilir ve destination pixel yalnız kendi source footprint'inde gerçekten bulunan palette indexlerinden birini seçebilir; yeni renk/index üretilmez.
- İlk analiz pass'i connected regions, contour/edge, ince çizgi (thin-line/skeleton adayları), corner, endpoint ve junction noktalarını çıkarır. Küçük connected component'ler ek importance alır; 1 px kontur ve küçük motiflerin majority vote ile kaybolması engellenir.
- Destination seçimi tam sayı blok oylaması yerine continuous source-cell overlap alanı + feature importance skoru kullanır. Coverage büyük bölgeleri kararlı tutarken thin-line/corner/junction skoru yapısal detayın background tarafından ezilmesini önceler.
- Global color-area correction, source renk oranından beklenen destination pixel sayılarını hesaplayıp yalnız source footprint tarafından desteklenen alternatifler arasında düşük maliyetli düzeltme yapar. Ancak topology önceliklidir: kurtarılmış thin line/corner/endpoint/junction hücreleri kilitlenir ve son area pass bunları tekrar silemez.
- Connected-region preservation her source component için hedefte temsil edilebilir bir hücre arar; özellikle küçük/critical motifler büyük background component'inden önce işlenir.
- Projected thin-line adjacency, kısa lokal path'lerle tekrar bağlanır; ayrıca candidate tarafından desteklenen tek-pixel gap'ler onarılır. Böylece küçültme sonrası oluşan kısa kopuklukların azaltılması hedeflenir.
- Feature/region analizi **repeat-aware** çalışır: sol-sağ ve üst-alt source border komşu kabul edilir. Raport sınırından geçen motif/çizgiler iki bağımsız kenar gibi değerlendirilmez.
- Pure enlargement RugScale'ın hedef problemi değildir; iki eksen de büyüyorsa categorical nearest-neighbour davranışına düşer. Mevcut `Edge Smooth` büyütme seçeneği ayrı kalır.
- Resize dialogundaki pixel işlemleri `Task.Run` ile WPF dispatcher dışına taşındı. İşlem sırasında seçenekler/OK/Cancel geçici olarak kilitlenir ve indeterminate progress gösterilir; sonuç UI thread'e dönüldüğünde mevcut `ReplaceDocument` yolu üzerinden tek Undo adımı olarak uygulanır.
- Core regresyon testlerine RugScale'ın kaynakta kullanılmayan renk üretmemesi, pure enlargement'ta categorical davranış, 1 px konturu Dominant'ın kaybettiği durumda koruması, tek-pixel küçük motifin tamamen kaybolmaması, ince L motifinin connected kalması ve opposite repeat borders sürekliliği eklendi.
- Handbook içindeki Design > Resize konusu RugScale'ın motif/topoloji koruma davranışıyla güncellendi.


### RugScale gerçek halı deseni kalibrasyonu — progressive resize (2026-09-22)

- İlk gerçek benchmark `B054H_BEIGE_48x50` ailesiyle yapıldı: nominal 200×300 orijinal (959×1499 px), 250×350 büyütme (1199×1750 px), 160×230 küçültme (765×1149 px) ve 120×180 küçültme (575×899 px). Tüm dosyalar 8-bit indexed ve aynı beş palette indexini (1,2,3,4,10) koruyor.
- Gerçek tasarım akışı tek seferde 200→120 değildir; normal kullanım **200→160→120 gibi kademeli resize** şeklindedir. RugScale kalite kriteri bu nedenle her adımda yeniden detail boost biriktirmemeli, progressive stability sağlamalıdır.
- İlk motor sürümünde ana beige/background index 10 oranı yaklaşık %50,23 → %47,81 → %44,10'a düşerken edge yoğunluğu yaklaşık %72,9 → %81,3 → %88,2'ye yükseldi. Bu, motif kaybından ziyade thin-detail over-preservation / kalınlaşma ürettiğini gösterdi.
- Düzeltme: feature ağırlığı artık footprint/shrink şiddetine göre adaptiftir. Normal kademeli küçültmeler daha konservatif importance kullanır; ağır tek-adım shrink daha yüksek yapısal koruma alır.
- Sıradan thin-line hücreleri artık kalıcı lock değildir. Corner/endpoint/junction keypoint'leri kilitli kalır; area correction ise bir hücreyi yalnız yerel same-color bağlantısını koparmıyorsa değiştirebilir (`WouldDisconnectColor` local topology guard). Böylece redundant kalınlık azaltılabilirken tek-pixel bridge korunur.
- Area correction'ın yeni renk adacıkları üretmemesi için alternate color normalde komşu region desteği ister; yalnız source-footprint skoru çok güçlü ise yeni local seed kabul edilir. Bu, küçültmede component fragmentation/speckle artışını sınırlamak içindir.
- 250×350 testinde renk oranları stabil kalmasına rağmen eski top-left anchored nearest mapping simetriyi bozuyordu: %100 yatay/dikey simetrik kaynak yaklaşık %69 pixel eşleşmesine düşebiliyordu. RugScale pure enlargement fallback'i pixel-center nearest sampling'e çevrildi; non-integer enlargement'ta mirror symmetry korunur.
- Regression testine RugScale enlargement mirror-symmetry kontrolü eklendi.


### RugScale progressive fidelity ayrımı — gerçek 160×230 testi (2026-09-22)

- İkinci gerçek test turunda 250×350, 160×230 ve 120×180 v1 çıktıları incelendi. Kullanıcı önceliği **küçük ebat, özellikle 200×300 → 160×230 sonucunun orijinale mümkün olduğunca benzemesi** olarak netleştirdi; 250×350 enlargement ayrı optimizasyon konusu olarak ertelendi.
- 160×230 v1 sonuç center-nearest baseline'dan yaklaşık %11,5 pixel farklılaşıyor ve ana beige/index 10 oranı yaklaşık %50,23 kaynaktan %48,92'ye iniyordu. Bu ılımlı küçültme için RugScale müdahalesinin hâlâ fazla olduğunu gösterdi.
- Yeni iki-yollu motor kontratı:
  - `minimumScale >= 0.72`: **Progressive/Conservative RugScale**. Gerçek 200→160→120 çalışma zincirinin her adımı bu sınıfa girer. Pixel-center nearest baseline üretilir; global area forcing, complete thin-line repaint ve projected skeleton reconstruction çalışmaz. Yalnız yüksek güvenli corner/endpoint/junction kaybı ve destekli tek-pixel gap onarılır.
  - `minimumScale < 0.72`: **Heavy RugScale**. Büyük tek-adım küçültmede full thin-line locking, region preservation ve projected connectivity korunur.
- Progressive path'te kaynak exact horizontal/vertical mirror symmetry taşıyorsa bu bir hard structural constraint kabul edilir. Lokal onarımlar mirrored 2/4-cell orbitler halinde, bütün source footprint'lerin ortak desteklediği palette index seçilerek uzlaştırılır. Bu işlem heap allocation üretmeden `stackalloc` orbit buffer ile yapılır.
- Test kontratı da ayrıldı: RugScale enlargement artık eski top-left `NearestNeighbor` ile birebir eşit olmak zorunda değildir; ayrı mirror-symmetry testi vardır. Heavy shrink testleri 1px contour, connected L ve repeat-border korunmasını doğrular. Progressive shrink için center-nearest baseline'dan en fazla %5 sapma regresyon sınırı eklendi.


### RugScale gerçek boyutta StackOverflow düzeltmesi (2026-09-22)

- 48x50 kalite gerçek rasterında progressive analiz sırasında uygulamanın doğrudan kapanmasının nedeni RugScale içindeki per-pixel stack-backed buffer kullanımlarıydı. Özellikle gap-repair döngülerindeki `Span<(...)> = [...]` collection expression'ları ve symmetry pass içindeki pixel-loop `stackalloc int[4]`, yüz binlerce iteration boyunca aynı method stack frame'inde birikerek `StackOverflowException` oluşturabiliyordu. Bu exception normal `try/catch` ile güvenli şekilde yakalanamadığından process kapanıyordu.
- Gap neighbour çiftleri class-level reusable `GapNeighbourPairs` dizisine taşındı. Symmetry orbit `stackalloc int[4]` yalnız method başında bir kez ayrılıyor ve her destination pixel'de tekrar kullanılıyor.
- RugScale dosyasında kalan stack kullanımları loop başına büyüyen pattern taşımıyor: projected-connectivity direction span'i method başına bir kez, local-topology ring'i ise ayrı helper call frame'inde kısa ömürlüdür.
- Core testlerine 480x720 -> 384x552 gerçekçi progressive raster regresyonu eklendi; amaç per-pixel stack büyümesinin geri gelmesini erken yakalamaktır.


### RugScale V2 branch/endpoint kaybı düzeltmesi (2026-09-22)

- Gerçek `B054H_BEIGE_48x50_160x230_v2.bmp` sonucu V1'e göre center-nearest baseline'a daha yakındı (yaklaşık %11,46 → %8,12 fark) ve mirror symmetry %100 oldu; buna rağmen bazı floral dallar kısalıyor veya ortada kesik kalıyordu. Bu nedenle yalnız global pixel-difference metriğinin kaliteyi temsil etmediği doğrulandı.
- Progressive keypoint korumasındaki hata düzeltildi: endpoint/junction için `HasColorNearby(..., radius:1)` artık "korunmuş" sayılmaz. Dal ucu bir hücre içeri çökmüş olsa bile mapped endpoint hücresinin kendisi source-footprint desteği yeterliyse geri kurulur. Corner için 1 hücrelik konum toleransı devam eder.
- Progressive path'e `RepairProjectedThinConnectivityProgressive` eklendi. Full skeleton repaint yapmaz; yalnız destination'da zaten yaşayan bir thin branch hücresinden, source'taki aynı-color thin adjacency boyunca yüksek destekli eksik hücreye propagation yapar. En fazla 5 bounded pass kullanır. Böylece hiç yaşamamış noise/detail resurrect edilmez, fakat mevcut dalın kırpılmış/kopmuş devamı tamamlanabilir.
- Structural repair hücreleri `locked` olarak işaretlenir. Exact-symmetric source için symmetry reconciliation artık bütün görüntüyü yeniden score etmez; yalnız repair sonrası gerçekten asymmetric olmuş 2/4-cell orbitleri uzlaştırır. Locked repair rengi bütün mirrored footprint'lerde destekleniyorsa karşı tarafa taşınır; aksi durumda mevcut orbit majority korunur.
- Yeni regresyon kontratları: moderate progressive shrink'te mapped branch endpoint kaybolamaz, branch tek connected component kalmalı; symmetry repair untouched center-nearest baseline'ı topluca yeniden boyayamaz.


### RugScale V4 — segment-aware branch graph (2026-09-22)

- Yeni renkli gerçek referans çifti benchmark olarak incelendi: 200×300 çizilmiş kaynak `MH1_B054A_HU211_0016.bmp` (960×2250) ve insan eliyle hazırlanmış 160×230 referans `MH1_B054A_HU211_0014.bmp` (768×1725). Aynı 8 palette indexini kullanıyorlar. Pixel-center nearest yalnız yaklaşık %61 birebir eşleşiyor; buna karşın palette alan oranları birbirine yakın. Sonuç: gerçek küçük-ebat çizimi salt nearest resample değildir; motif/topoloji yeniden düzenlemesi gerekir.
- Bu nedenle progressive kalite kontratındaki eski `%5 center-nearest sapma` hedefi kaldırıldı. Baseline yalnız başlangıçtır; öncelik branch endpoint, junction, connectedness, symmetry ve palette-safe motif bütünlüğüdür.
- `Analysis.Branches` eklendi. Source thin-line pikselleri same-color 8-neighbour graph olarak analiz edilir; degree ≠ 2 noktaları node kabul edilip aralarındaki yollar `BranchSegment` olarak çıkarılır. Degree=2 kapalı contour loop'lar bilinçli olarak segment listesine alınmaz; amaç dolu motif sınırlarını yeniden çizmek değil gerçek dal/twig/connectors korumaktır.
- Progressive RugScale artık `RepairBranchSegmentsProgressive` kullanır. Source branch segmenti target grid'e centre mapping ile projekte edilir, ardışık duplicate hücreler düşürülür ve yalnız source footprint candidate desteği bulunan eksik centreline hücreleri geri kurulur. Endpoint ve junction temaslı segmentlerde daha düşük ama kontrollü support threshold kullanılır.
- Segment içinde iki yaşayan dal hücresi arasında tam bir tek-pixel hole varsa branch continuity competing local bridge'den üst öncelik alır; exact source-segment desteği varsa hole doldurulur. Bu düzeltme multi-bend branch'in iki connected component'e bölünmesini engelledi.
- V4 regresyon testleri eklendi: multi-bend branch endpoint + connectedness, T/junction dört endpoint + tek component, gerçekçi progressive raster stack safety, mapped endpoint preservation ve symmetry repair.
- Geçici GitHub Actions doğrulamasıyla `RugCAD.Core.Tests` Release konfigürasyonunda çalıştırıldı. İlk V4 turunda 148 testten 147'si geçti; multi-bend branch iki component'e ayrılıyordu. One-cell projected branch hole düzeltmesinden sonra ikinci tur **148/148 başarılı** oldu (2.5364 sn). Geçici workflow test sonrası repodan kaldırıldı.


### RugScale V4 gerçek-raster performans optimizasyonu (2026-09-22)

- `960×2250 → 768×1725` gerçek kullanım boyutunda ilk V4 branch-graph uygulaması kullanıcı makinesinde 3 dakikayı aşabiliyordu. Task Manager'da RugCAD ~%22 CPU ve ~275 MB RAM kullanıyordu; process kilitli değildi ancak algoritma etkileşimli kullanım için kabul edilemeyecek kadar yavaştı.
- Ana darboğaz `BuildThinBranchSegments` içindeki milyonlarca `HashSet<long>` edge işlemi ve detaylı desende ordinary contour edge'lerin fazla geniş `Thin` sınıfına girmesiydi.
- Branch graph edge bookkeeping `HashSet<long>` yerine source-pixel başına 8-bit `adjacency` + 8-bit `visited` mask yapısına çevrildi. Her undirected edge dört forward direction ile yalnız bir kez oluşturulur; branch walk bit mask üzerinden O(N) ilerler.
- İki hücrelik node-to-node temasları artık `BranchSegment` olarak allocate edilmez; progressive repair zaten bunları kullanmıyordu.
- Branch graph sadece sparse local neighbourhood (`SameNeighbourCounts <= 4`) taşıyan true line-like pixel adaylarında kurulur. Feature pass'te zaten hesaplanan `same8` değeri `Analysis.SameNeighbourCounts` içinde cache edilir; graph için ikinci 8-neighbour scan kaldırıldı.
- Progressive resize (`minimumScale >= 0.72`) full connected-region BFS kullanmadığı için `Analyze(... includeRegions:false, buildBranches:true)` yoluna ayrıldı. Heavy shrink region/topology yolu eski region analizini kullanmaya devam eder.
- Optimizasyon sonrası `RugCAD.Core.Tests` Release doğrulaması **148/148 başarılı** oldu. 480×720 progressive regression fixture CI'da yaklaşık 255 ms tamamlandı.
- Geçici benchmark workflow'u aynı hedef ölçüde, yoğun thin-branch/junction/border içeren deterministic sentetik `960×2250 → 768×1725` deseni çalıştırdı: **741 ms** (GitHub Actions Ubuntu Release). Bu değer gerçek halı deseninin birebir süresi değildir, ancak eski dakika-mertebesi branch graph darboğazının kaldırıldığını doğrular.
- Geçici CI/benchmark workflow dosyası ölçüm sonrası repodan kaldırıldı.


### RugScale V5 — repeat/rapport border stabilization (2026-09-22)

- Gerçek çizilmiş 160×230 referans ile RugScale çıktısının yan yana incelemesinde branch continuity V4 ile iyileşmiş olsa da border üzerindeki tekrar eden motiflerin her occurrence'ta bağımsız resample edildiği, repeat phase'in kaydığı ve bazı tekrarların farklı genişlik/şekle dönüştüğü görüldü. Bu nedenle repeat artık yalnız wrap-neighbour bilgisi değil **hard structural constraint** olarak ele alınır.
- Progressive RugScale'a `RepeatBand` / `RepeatBandSide` modeli ve `DetectOuterRepeatBands` analizi eklendi. Top/Bottom/Left/Right dış bantlar kendi eksenlerinde indexed-pixel autocorrelation ile taranır. Detector en az 4 tam repeat ister ve en yüksek korelasyonun %94'ü içinde kalan **en küçük** periyodu seçerek 160/240 gibi harmonic'ler yerine fundamental repeat'i tercih eder.
- Detector corner-specific geçişleri periyot hesabından çıkarır, yeterli renk varyasyonu olmayan düz bantları repeat kabul etmez ve küçük micro-border'ların mükemmel korelasyonla ana dekoratif bordürü ezmesini önlemek için band-thickness ağırlığı kullanır.
- Gerçek 200×300 `MH1_B054A_HU211_0016.bmp` kaynağında aynı detector mantığı ayrıca doğrulandı: üst bordür yaklaşık **80 px / 10 repeat**, alt bordür **80 px / 9 repeat**, sağ dikey bordür **120 px / 16 repeat** seçiyor. Bu değerler görseldeki gerçek motif aileleriyle uyumludur.
- `StabilizeRepeatBandsProgressive` her detected band için corner blokları dışında merkezi bir source repeat tile seçer. Target repeat alanında kaynak repeat adedi korunur; toplam target span bu adede bölünür ve her occurrence aynı canonical tile phase'inden örneklenir. Böylece resize rounding farkları occurrence'lar arasında birikmez; bir tile 1 px geniş/dar olsa bile motif fazı drift etmez.
- Repeat stabilizasyonu V4 branch/keypoint repair'den sonra çalışır. Önceden structural repair ile kilitlenen hücreler değişmez. Güçlü repeat korelasyonunda canonical repeat rengi target cell'in exact source-footprint shortlist'ine düşmese bile aynı doğrulanmış source tile'dan geldiği için kontrollü phase correction yapılabilir; zayıf repeat tespitlerinde candidate desteği zorunlu kalır.
- Corner blocks repeat strip dışında bırakılır. Böylece köşeye özel rozet/geçiş motifleri düz border repeat'i ile ezilmez; V5 ilk aşamada corner motifini yeniden tasarlamak yerine kaynak/baseline corner geometrisini korur.
- Core regresyonlarına yatay border repeat phase + corner preservation ve dikey border repeat phase + corner preservation testleri eklendi. Release CI sonucu **150/150 test başarılı**; V5 horizontal/vertical repeat testleri ayrı ayrı geçti.


### RugScale V5.1 — repeat/rapport phase hardening (2026-09-22)

- Gerçek 200×300 kaynakta V5 detector analizi üst/alt dış border için aynı 80 px fundamental period'u bulmasına rağmen eski span hesabı band kalınlığını corner extent'e kattığı için üstte 10, altta 9 repeat üretebiliyordu. Repeat span artık sampled strip thickness'ten bağımsız, motif period'u üzerinden hesaplanıyor; aynı period karşılıklı kenarlarda aynı repeat count/phase üretir.
- `HarmonizeOppositeRepeatBands` eklendi. Exact top-bottom mirror symmetry varsa üst/alt; exact left-right symmetry varsa sol/sağ aynı fundamental period'a zorlanır. Simetrik olmayan tasarımda yalnız period'lar birbirine %6 civarında yakınsa harmonizasyon yapılır.
- Canonical repeat artık tek bir orta source tile'dan kopyalanmıyor. `BuildCanonicalRepeatTile` her phase/cross hücresi için bütün source occurrence'lar arasında palette-index majority çıkarır; tek bozuk/elle farklı occurrence bütün border'a yayılmaz. Middle occurrence yalnız majority tie-break olarak kullanılır.
- Canonical tile her hücre için consensus/confidence da üretir. `band.Score >= 0.80 && consensus >= 0.72` olduğunda repeat evidence lokal branch/keypoint lock'tan daha yüksek öncelik alır. Bu, doğrulanmış raport bandında tek bir lokal topology kararının repeat motifini farklı bırakmasını engeller.
- Repeat-authoritative hücreler local candidate shortlist dışında kalsa bile aynı doğrulanmış source repeat tile'dan geldiği için uygulanabilir; zayıf repeat detections yine normal candidate support ister.
- Yeni regresyon: `Scale_RugScale_V5_CanonicalRepeatUsesMajorityOccurrence`. Sekiz occurrence'dan biri kasıtlı bozuluyor; hedef repeatlerin tamamı majority motifte kalmalıdır.
- CI Release doğrulaması: **151/151 test başarılı**, horizontal repeat, vertical repeat ve majority-canonical testleri geçti. Geçici workflow doğrulama sonrası repodan kaldırıldı.


### RugScale V6 — multi-zone inner repeat detection (2026-09-22)

- V5.x yalnız outer border repeatlerini güvenilir şekilde stabilize ediyordu; farklı desenlerde repeat iç bordürde veya tasarımın yalnız belirli yatay/dikey bölümünde olduğunda etkisi düşüyordu. V6 repeat modeli arbitrary cross-axis zone destekleyecek şekilde genelleştirildi.
- `RepeatBand` artık `CrossStart` ve `IsOuter` taşır. Horizontal band için CrossStart source Y; vertical band için source X koordinatıdır. Stabilizer target cross-start/end'i source→target ölçeğinden hesaplar; repeat zone artık kenardan başlamak zorunda değildir.
- Progressive akış `DetectOuterRepeatBands` yerine `DetectAllRepeatBands` kullanır. Önce specialized outer detector çalışır; ardından horizontal ve vertical inner repeat zone'lar aranır.
- Inner detector tek bir full-axis varsayımı yapmaz. Full, first-half, second-half ve central search windows üzerinde line-period probe çıkarır; komşu cross-line probe'ları period ailesine göre cluster'layıp 2D zone correlation ile doğrular. Böylece merkezi açıklık/medallion tarafından kesilen iç border repeatleri ayrı zone olarak yakalanabilir.
- Period araması coarse (~100 candidate) + local refine şeklindedir. Fundamental/harmonic kontrolü artık bütün alt periodları tekrar taramak yerine yalnız olası divisor/harmonic adaylarını kontrol eder; gerçek rasterda gereksiz correlation maliyeti sınırlandı.
- Inner zone'lar overlap/confidence arbitration'dan geçer. Specialized outer repeat rectangle ile çakışan inner zone period ne olursa olsun outer'ı override edemez. Birbirine büyük ölçüde binen farklı inner-period hipotezlerinden yalnız belirgin şekilde daha güçlü olan tutulur.
- Stabilization order score-descending, outer-tie-priority şeklindedir. Strong repeat consensus mevcut branch/keypoint lock'u yalnız aynı zone kendi yetki alanındaysa override eder; iç detector dış border'ı yeniden boyayamaz.
- Yeni regresyonlar: `Scale_RugScale_V6_PreservesInnerHorizontalRepeatZone` ve `Scale_RugScale_V6_PreservesInnerVerticalRepeatZone`. Outer V5 phase + majority-canonical testleri de korunmuştur.
- Release CI doğrulaması: **153/153 test başarılı**. V6 horizontal/vertical inner repeat testleri ve V5 outer-repeat testleri birlikte geçti.
- Performans: 480×720 progressive regression fixture yaklaşık **509 ms**; outer + inner repeat zone'lar ve yoğun field ornament içeren deterministic `960×2250 → 768×1725` sentetik benchmark **3651 ms** (GitHub Actions Ubuntu Release). Bu gerçek Windows halı dosyasının birebir süresi değildir ancak dakika-mertebesi regresyon olmadığını doğrular.
- Geçici CI/benchmark workflow doğrulama sonrası repodan kaldırıldı.


### RugScale gerçek desen analizi — designer hard constraints (2026-09-22)

İki farklı gerçek halı ailesi birlikte incelendi ve algoritmanın artık tek örneğe göre değil, halı desen çizim kurallarına göre davranması için aşağıdaki hard constraint'ler eklendi.

#### B054A gerçek 160×230 referans karşılaştırması

- İnsan eliyle çizilmiş `MH1_B054A_HU211_0014.bmp` ile önceki RugScale `B054A_48x75_160x230_v3.bmp` aynı 768×1725 indexed raster üzerinde karşılaştırıldı.
- Pixel-level fark yaklaşık **%39,78**. Bu yüksek fark tek başına hata metriği değildir; elle çizilmiş küçük ebat, büyük ebadın nearest-resample kopyası değildir.
- İnsan referansın edge yoğunluğu yaklaşık **%44,56**, eski RugScale V3'ün yaklaşık **%42,92** idi. V3 bazı mikro kontur/dalları gereğinden fazla sadeleştiriyordu.
- En kritik bulgu simetriydi. 200×300 kaynak tam piksel mirror testinde simetrik görünmüyordu; ancak ±1/±2 px grid-phase telafisiyle **sol-sağ ve üst-alt yapısal simetri %100** bulundu. İnsan çizilmiş 160×230 sonuç da grid-phase-aware ölçümde yaklaşık **%100 / %100** simetrikti.
- Eski RugScale V3 aynı ölçümde yalnız yaklaşık **%80,2 sol-sağ / %81,9 üst-alt** seviyesinde kalıyordu. Resize işlemi kaynağın tasarım simetrisini bozuyordu.
- İnsan referansta bazı palette component'leri V3'e göre daha az parçalı ve daha büyük/okunur bağlı motifler oluşturuyordu; V3 özellikle bazı renklerde gereksiz fragment üretirken bazı renkleri de yanlış birleştiriyordu.

#### B163A farklı desen karşılaştırması

- Kaynak `CERP-MD2_B163A_PD277_0016_org.bmp` (960×1500) ve RugScale sonucu `CERP-MD2_B163A_PD277_0016_160x230_test1.bmp` (768×1150) ayrı bir desen ailesi olarak analiz edildi.
- RugScale sonucu center-nearest baseline'dan yalnız yaklaşık **%9,46** farklıydı; edge yoğunluğu da baseline ile neredeyse aynıydı (~%38,75 baseline, ~%38,83 RugScale). Dolayısıyla görünen bozukluğun nedeni blur/degrade değil, yanlış yapısal kararlar idi.
- Kaynak grid-phase-aware ölçümde sol-sağ **%100**, üst-alt yaklaşık **%97,7** simetri taşıyordu. Eski RugScale sonuçta bu değerler yaklaşık **%84,8 / %86,0** seviyesine düşüyordu.
- Index 7 için en büyük connected component center-nearest baseline'da yaklaşık **45 bin px**, RugScale sonucunda yaklaşık **84 bin px** oldu. Bu, source'ta ayrı kalması gereken motiflerin resize repair sırasında yanlış bridge/junction ile birleşebildiğini gösterdi.

#### Hard-coded designer rules

1. **Grid-phase symmetry detection**
   - Symmetry artık yalnız `x ↔ width-1-x` birebir eşitliğiyle ölçülmez.
   - ±3 px mirror phase denenir; pixel agreement ile structural edge/thin/corner/endpoint/junction agreement birlikte ölçülür.
   - Hard symmetry yalnız çok güçlü durumda (`pixel >= 0.992`, `structural >= 0.985`, structural coverage >= 0.20) etkinleşir. Böylece sparse T/branch gibi küçük şekiller bütün document'i yanlışlıkla mirror constraint'e sokmaz.

2. **Designer-centred target symmetry**
   - Source'taki phase shift simetrinin varlığına kanıttır; target'a aynen offset olarak kopyalanmaz.
   - Küçük ebat, gerçek desen çizicisinin yaptığı gibi geometrik merkeze yeniden oturtulur.
   - Indexed BMP'deki teknik bir-pixel sentinel/seam kenarı uniform + globally rare ise symmetry content alanının dışında tutulur.

3. **Symmetric orbit voting**
   - Symmetric target orbit (2/4 cell) rengi sadece current majority ile değil; her hücrenin source-footprint `CandidateSet` desteği, locked structural repair evidence ve mevcut renk oyu birlikte kullanılarak seçilir.
   - Böylece tek bir bozuk quadrant körlemesine diğer quadrantlara kopyalanmaz.

4. **Unsupported junction/merge pruning**
   - Verified repeat zone dışında, repair sonucu oluşan bir hücre aynı rengin 3+ ayrı neighbour arc'ını birleştiriyorsa bu yeni T/X junction kabul edilir.
   - Mapped source footprint'te aynı palette index için gerçek `FeatureFlags.Junction` desteği yoksa ve center-source baseline alternatifi yeterli candidate desteğine sahipse junction geri alınır.
   - Repeat canonical tile'ları bu filtreden muaftır; güçlü raport consensus kendi zone'u içinde authoritative evidence sayılır.

5. **Repeat + topology + symmetry priority**
   - Multi-zone repeat detection outer ve inner yatay/dikey raportları bulur.
   - Verified repeat consensus zone içinde lokal keypoint kararından üstündür.
   - Repeat dışı field/motif alanında branch topology ve unsupported-merge guard geçerlidir.
   - Hard designer symmetry en son uygulanır ve bütün structural repair'leri palette-safe symmetric orbit olarak uzlaştırır.

#### Regression / performance

- Yeni testler:
  - phase-shifted left-right symmetry + technical sentinel
  - phase-shifted top-bottom symmetry target re-centering
  - independent motif arms'ın unsupported T/X junction olarak birleşmemesi
  - mevcut V4 branch, V5 outer repeat ve V6 inner repeat regresyonlarının tamamı
- Final Release CI: **156/156 test başarılı**.
- Realistic 480×720 progressive regression yaklaşık **580 ms**.
- Symmetric outer+inner repeat, field ornament ve sentinel edge içeren deterministic `960×2250 → 768×1725` benchmark yaklaşık **3023 ms** (GitHub Actions Ubuntu Release).
- Geçici final validation workflow doğrulamadan sonra repodan kaldırıldı.


### RugScale V7 — Motif Atlas Shrink (2026-09-22)

- RugScale progressive shrink yaklaşımı pixel-first repair'den motif-first reconstruction'a genişletildi.
- Yeni `src/RugCAD.Core/Drawing/RugScale/MotifShrinkEngine.cs` source indexed design içindeki same-color 8-connected motif primitive'lerini kataloglar.
- Büyük background / frame / sentinel componentleri structural backbone olarak ayrılır; dekoratif küçük/orta componentler target'ta yeniden projekte edilir.
- Her motif için bbox, alan, boundary yoğunluğu, baskın çevre rengi ve target yerleşim geometrisi tutulur.
- Aynı geometri farklı palette indexlerle tekrar ediyorsa palette bağımsız 16×16 normalized binary signature + aspect/fill bucket ile aynı shape-family altında gruplanır.
- Aynı family ve benzer source extent için target binary mask yalnız bir kez üretilip cache'lenir; her occurrence kendi palette indexiyle aynı geometriyi boyar.
- Motif target maskeleri inverse sampling yerine source pixel-centre forward projection ile oluşturulur; connected source motifin target sample kaçırması yüzünden parçalanması azaltılır.
- Tiny/line-like motifler normal rounding ile tamamen çökecekse bounded minimum-detail extent uygulanabilir; motif göreceli olarak büyütülmez.
- Centre-nearest'ten kalan stale motif parçaları source'taki dominant surrounding colour ile temizlenip target motif yeniden boyanır.
- Repeat precedence bütün borderı kilitlemez. İlk V5 outer-repeat pass'inin gerçekten değiştirdiği target hücreler snapshot-diff ile korunur; motif atlası geri kalan dekoratif alanda authoritative kalır.
- Küçük rasterlarda false outer-border detection'ı engellemek için outer repeat detector cross-axis minimumu 64 px yapıldı.
- Yeni regressionlar:
  - `Scale_RugScale_MotifAtlas_ReusesGeometryAcrossDifferentColors`
  - `Scale_RugScale_MotifAtlas_KeepsSeparatedTinyMotifsRepresented`
- Final Core doğrulama: **158/158 başarılı**.
- Deterministic Release benchmark:
  - `960×2250 → 768×1725`: yaklaşık **3127 ms**
  - `960×1500 → 768×1150`: yaklaşık **1129 ms**
- V7 yalnız shrink'te aktiftir. Motif-aware enlargement gelecekte ayrı bir seçenek/pipeline olarak yapılacaktır.


### RugScale V8 — motif memory, feedback repair ve viewport continuity (2026-09-22)

- RugScale için kalıcı ve taşınabilir motif knowledge katmanı eklendi: `MotifMemoryFile`, `MotifFamilyMemory`, `MotifDescriptor` ve JSON serializer. Runtime dosya `%AppData%\\RugCAD\\rugscale-motif-memory.json`; Import/Export ile kullanıcı ve geliştirme tarafı arasında taşınabilir.
- Descriptor palette-index bağımsız color-role + edge signature taşır; rotate/mirror canonicalization yapar ve Warp/Weft üzerinden fiziksel aspect'i hesaba katar.
- `MotifRepairEngine` preview'da seçilen bozuk motifi original source içinde arar. Source geometry target ebatta yeniden rasterize edilir; source palette renklerini kör kopyalamak yerine source-role → target-role mapping ile mevcut renk varyasyonu korunur.
- Relative rotate/mirror transform family canonicalization'dan ayrı çözüldü. Aynı motif döndürülmüş veya aynalanmış instance olarak bulunduğunda source patch target orientation'a çevrilerek uygulanır.
- `MotifSourceCatalog` eklendi. Session boyunca source'tan `Branch`, `LeafLike`, `Primitive`, `LargePart`, `Compound` entry'leri çıkarılır; structural background/frame componentleri hariç tutulur. Uzun tek-pixel çizgiler ayrıca Branch kabul edilir.
- Matcher önce exact source catalogue entry'lerini puanlar; segmentation'ın yetersiz olduğu seçimlerde bounded sliding-window fallback devam eder.
- Resize > RugScale shrink preview artık gerçek `DesignCanvas` kullanır. `Click part` ve mevcut point-by-point `Polygon` selection ile bozuk motif alanı seçilebilir.
- Candidate pending halde gösterilir. **Good / learn** accepted repair'i preview zincirine ekler ve positive feedback yazar; **Bad / next** family confidence'ını düşürüp sonraki source adayına geçer. Pending candidate doğrudan memory'ye yazılmaz.
- Learned family confidence sonraki candidate ranking'de bounded tie-break olarak kullanılır; source structural evidence'ın yerine geçmez.
- Packaged seed memory eklendi. İlk seed B054A `_0014` referans ailesi ve B163A original'dan çıkarılan descriptor family/sample bilgisini taşır; raw carpet bitmap embed edilmez. Existing user memory üzerine merge edilir.
- Preview navigation workspace ile aynı kalır. Ctrl+wheel, wheel, Shift+wheel, Pan/Alt+Space/middle mouse ve mevcut configurable `pan.*` / `view.zoom*` shortcutları kullanılır. Ayrı RugScale shortcut tablosu yoktur.
- Standalone Resize penceresinde `DesignCanvas.HandleDrawingKey` önce çalıştırılır; Polygon Enter/Escape/Backspace ve selection davranışı MainWindow canvas ile aynı precedence'e sahiptir.
- `DesignCanvas.ViewportState` eklendi. Candidate gösterme, Good/Bad, accepted repair recomposition ve preview document swap sırasında zoom + normalized viewport centre korunur; kullanıcı her işlemde tekrar motif bölgesini aramak zorunda kalmaz.
- Regression kontratları: motif memory JSON round-trip/feedback, palette-independent + rotated family match, Warp/Weft physical aspect, rotated geometry + target color variant, hierarchical source catalogue.
- Doğrulama: RugScale/MotifMemory/MotifAtlas filtreli Release Core **27/27**; tam Windows build başarılı, tam Core **164/164**, App regression **83/83**.


### RugScale motif detector — uzak compound adayının local source'u ezmesi düzeltildi (2026-09-23)

- Kullanıcı gerçek B163A/RugScale preview testinde detector'ın seçilen motif yerine source'un başka bir bölümündeki alakasız çok-parçalı bir `Compound` yapıyı ilk aday olarak gösterebildiğini doğruladı. Sağdaki masked source preview bu hatayı görünür kıldı: descriptor benzerliği yüksekti fakat semantik motif yanlıştı.
- Kök neden iki parçalıydı. Birincisi, candidate skorunda source konumu yalnız küçük bir prior'dı; descriptor/alignment ve catalogue `Compound` bonusu uzak lookalike'ı fazla yükseltebiliyordu. İkincisi, source catalogue içinde herhangi bir yerde güçlü aday bulunması bütün rectangular fallback taramasını kapatıyordu; böylece target selection'ın inverse-mapped original-source çevresi hiç score edilmeden kalabiliyordu.
- `MotifRepairEngine.FindCandidates` scoring yeniden dengelendi: descriptor family, relative transform, motif mass ve inverse-mapped source-position prior birlikte base score'a giriyor. Büyük additive mass/catalogue bonusları azaltıldı; `Compound` artık yalnız küçük tie-break.
- Local ve global fallback birbirinden ayrıldı. Inverse-mapped source çevresindeki local fallback artık her detect işleminde daima score edilir; güçlü catalogue candidate yalnız pahalı global fallback taramasını kapatabilir. Ayrıca catalogue exact-mask ve rectangular fallback aynı `(x,y,w,h)` bbox'a sahip olsa bile `seen` anahtarında ayrı hypothesis tutulur; over-grouped `Compound` aynı bbox üzerinden local fallback'i susturamaz.
- Yeni regresyon `MotifRepair_DistantCatalogueLookalikeCannotHideInverseMappedSourceArea`: catalogue'a kasıtlı olarak yalnız uzak, birebir benzeyen bir motif verilir; detector buna rağmen inverse-mapped source çevresini fallback ile tarayıp ilk adaya local source'u koymalıdır.
- Değişiklikler yalnız `chatgpt/rugcad-work-2026-09-22` branch'ine yazıldı; `main` değiştirilmedi.


### RugScale B163A motif-by-motif GitHub Actions kalibrasyonu (2026-09-23)

- Kullanıcının verdiği gerçek `CERP-MD2_B163A_PD277_0016_org.bmp` (960×1500, quality 48×50)
  lossless fixture olarak CI'a alındı; aynı kalite 160×230 hedefi 768×1150 olarak otomatik üretiliyor.
- Draft PR #5 altında iki bağımsız workflow var: `RugScale B163A motif audit` ve
  `RugScale B163A real-design validation`. `main` değiştirilmedi.
- Motif audit source catalogue'daki 40.921 usable motifin tamamını tarıyor; output BMP, Nearest BMP,
  `motifs.csv`, `detector.csv`, `report.json` ve `report.md` artifact olarak yükleniyor.
- Full Core regression **172/172 başarılı**.
- Detector source ranking gerçek raster sample'larında **16/16** ve ayrı validation sample'ında
  **20/20 top-1 local-source** verdi. Önceki uzak `Compound` motifin selected source'u ezmesi
  problemi bu testlerle koruma altına alındı.
- Final motif projection-retention: mean **%98,83**, median **%100**, P10 **%98,00**.
- Strict structural audit mean **%72,67**; mean delta vs Nearest **+0,1061**.
- B163A source left-right symmetry %100 ve target'ta %100 kaldı. Source top-bottom symmetry yaklaşık
  %97,70; önceki RugScale yaklaşık %86,31 iken candidate-backed soft symmetry ile **%89,64** oldu
  (Nearest yaklaşık %86,75).
- Soft symmetry ilk denemesinde pass ordering hatası exact left-right symmetry'yi %96,2'ye düşürdü.
  Düzeltme: soft axes önce, exact hard axis en son; ayrıca near-symmetry target phase'i source'taki
  measured ±3px phase'den ölçekleniyor ve yalnız source mirror pair gerçekten aynı index ise
  reconciliation yapılabiliyor.
- Son audit runner resize süresi yaklaşık **3,34 sn**. Palette-safe kontratı korunuyor; yeni index yok.
- Strict audit'teki en zayıf kayıtların büyük kısmı 1×3/1×4 tiny Branch primitive'leri. Bunlarda exact
  cell-phase skoru düşük olsa da radius-1 motif retention ~%98 seviyesinde; bundan sonraki branch
  geliştirmeleri strict metrik uğruna geometry'yi yanlış target hücresine zorlamamalı.


### RugScale çizim/redraw self-training (2026-09-23)

- Gerçek B163A source rasterı teacher olarak kullanılarak çizim/redraw tarafı için self-supervised
  training workflow eklendi: `RugScale B163A drawing self-training`.
- Her sample beş `MotifRepairStyle` ile tekrar source-first çiziliyor; expected projected source
  mask/color'a karşı F1 + IoU + indexed-color + boundary retention ile ölçülüyor.
- İlk başarılı training: **242 sample / 122 motif family**. Branch'te 56/60 ve LeafLike'ta 57/60
  `Balanced`; Compound'da 30/60 `PreserveBranches` + 25/60 `DetailAndConnectivity`;
  Primitive'de 41/60, LargePart'ta 2/2 `DetailAndConnectivity` winner.
- Overall `DetailAndConnectivity` mean %92,02 ile en yüksek; `ContourFirst` B163A setinde
  0 winner.
- Bu ölçüm redraw cold-start order'a uygulandı. `Compound` artık PreserveBranches → Detail;
  `Primitive/LargePart` DetailAndConnectivity ile; Branch/LeafLike Balanced ile başlıyor.
- Core'a `MotifRepairEngine.PreferredColdStartStyle(MotifKind)` eklendi; UI dışındaki direct
  repair caller'ları da trained prior kullanıyor.
- `MotifMemory` family-specific feedback trained cold-start prior'ın önünde kalmaya devam ediyor.
- Action artifact'ı `trained-memory.json`, `drawing-training.csv`, `drawing-training.md`
  üretiyor. `main` değiştirilmedi.


### N69 curve/arc kalite eğitimi + ebat büyütme (2026-09-23)

- Kullanıcının verdiği dört gerçek curve-ağırlıklı indexed N69 BMP fixture olarak CI'a eklendi:
  C071C 40×60, B996A 40×50, C004A 40×50, C069A 40×60.
- `CurveScaleEngine` artık yalnız enlarge helper değil, quality-aware curve reconstruction katmanı:
  skeleton çıkarımı + chamfer-distance **local pen width** + target path rasterization.
- Ana kural kod seviyesinde uygulandı: fiziksel ebat scale'i curve kalınlığını büyütmez;
  stroke width yalnız target/source warp-weft quality oranına bağlıdır.
- Source component tek kalınlıkla temsil edilmiyor. Skeleton'ın her pikselinde source pen width
  ölçülüyor; taper/widen/arc kalınlık değişimleri target'ta yeniden kullanılıyor.
- Shrink curve pass additive yapıldı. Baseline motif/repeat/topology sonucu silinmiyor; yalnız
  source-backed curve centreline geri restore ediliyor.
- Strong designer symmetry için transactional guard eklendi: curve pass mevcut güçlü symmetry
  sonucunu bozarsa baseline restore ediliyor. Bu guard B163A real-design regresyonunu tekrar yeşile
  getirdi.
- Exact symmetry yalnız shift=0 kabul edilmiyor; ±3 px teknik/sentinel phase exact symmetry de
  algılanıp target'ta hard constraint olarak korunuyor.
- `RugScale four-design curve suite` 80% shrink→roundtrip, Nearest baseline ve 160% same-quality
  direct enlargement ölçüyor. Son green run exact sonuçları: C071C %91.46, B996A %92.34,
  C004A %92.04, C069A %89.49; ±1px geometry sırasıyla %97.38/%97.93/%98.08/%97.26.
- C071C/B996A/C004A roundtrip exact metriğinde Nearest'ten daha iyi. C069A exact'te ~1 puan düşük,
  fakat palette drift %1.77 vs Nearest %2.82 ve ±1px fark yalnız ~0.11 puan; exact metriği uğruna
  çizimi bozacak overfit uygulanmadı.
- 160% same-quality direct enlargement source-guided curve redraw ile test ediliyor. 125% civarı
  moderate same-quality büyütmede redraw henüz baseline'ı geçmediğinden conservative path tutuldu;
  target kalite değiştiğinde ise curve her durumda yeni warp/weft grid'ine yeniden rasterize edilir.
- Multi-pixel regression eklendi: 3px Curve-tool benzeri yay fiziksel 2× büyütmede 6px blok stroke'a
  dönüşmemeli; component bağlı kalmalı ve palette index güvenliği korunmalı.
- Değişiklikler yalnız `chatgpt/rugcad-work-2026-09-22` branch'inde; `main` değiştirilmedi.


### Motif RugScale / Curve & Fill ayrımı — gerçek B996 görsel düzeltmesi (2026-09-23)

- Kullanıcının gönderdiği original B996 (640×1150) ile mevcut RugScale sonucu (800×1800) doğrudan
  yan yana incelendi. Kullanıcı tespiti doğru: curve'ler source gibi oval/düzgün değildi ve bazı
  filled ornament iç renkleri skeleton/redraw sırasında daralmış/ölmüş görünüyordu.
- Kök mimari hata kabul edilip geri alındı: `CurveScaleEngine` artık normal `ScaleMode.RugScale`
  execution path'ine bağlanmıyor. Motif tabanlı desenlerin motoru tekrar yalnız motif/topology.
- Yeni ayrı enum/UI modu: **`ScaleMode.CurveFill` / "RugScale Curve & Fill — outlined / curved
  filled designs"**. Bu mod yalnız kullanıcı seçerse çalışır.
- `CurveFillScaleEngine` her kullanılan indexed rengi dolu categorical region olarak işler;
  source warp/weft pixel aspect'i ile signed-distance contour çıkarır ve target'ta contour
  geometry'yi yeniden rasterize eder. RGB interpolation/yeni palette index yoktur.
- Bu motor filled leaf/ornament'i skeleton'a dönüştürmez; dış ve iç curve sınırları birlikte
  yeniden çizilir, iç bölge source'taki kendi palette index'iyle dolu tutulur. "İç rengi öldürme"
  sınıfındaki eski hata böylece tasarım seviyesinde engellenir.
- Resize UI'da artık iki ayrı uzman seçim var: `RugScale — motif & topology designs` ve
  `RugScale Curve & Fill — outlined / curved filled designs`.
- Four-design curve audit `ScaleMode.CurveFill`'e geçirildi. B996 gerçek kullanıcı senaryosu için
  640×1150 → **800×1800** ayrı artifact üretiliyor.
- Motif feedback/detect/repair paneli yalnız normal RugScale küçültmede kalır; Curve & Fill ile
  karıştırılmaz.
- Değişiklikler yalnız `chatgpt/rugcad-work-2026-09-22` branch'inde; `main` değiştirilmedi.


### Curve-tool inverse model / source-style training (2026-09-24)

- `ToolFaithfulCurveStyleLearner` eklendi. Trusted 1x1/Pixel-Cord chain artık kaynak rasterdan
  RugCAD'in gerçek `CurveRasterizer` family'lerine tersine fit ediliyor.
- Through-Points: source-through control index optimization + roundness search.
- Bezier: chord-length least-squares effective cubic handle recovery + raster-score local handle
  refinement. Historical Bezier/Spline slider roundness rasterdan tekil ayrışmıyorsa equivalent
  target geometry öğreniliyor.
- Deliberate corner, düz chain, kapalı outline veya düşük-güvenli fit otomatik graph fallback'te.
  "AI smooth olsun" diye source çizim karakteri zorla değiştirilmiyor.
- 60 örnek synthetic teacher self-training eklendi. Ana objective 160% target exact-raster
  extrapolation:
  - ThroughPoints: %83.52 vs graph %63.25 (**+20.27 puan**), accepted family precision 27/27,
    roundness MAE **0.046**.
  - Bezier: %77.73 vs %66.26 (**+11.47 puan**).
  - Spline/equivalent compact model: %68.31 vs %63.46 (**+4.85 puan**).
- Bu değerler artık CI guardrail: TP coverage/precision/roundness ve her family'de minimum target
  exact-F1 improvement kontrol ediliyor.
- Dört gerçek N69 direct run learned chain: C071C **292**, B996A **878**, C004A **367**,
  C069A **201**. Düşük-güvenli binlerce chain exact graph fallback.
- Aynı yönlü translated curve tekrarları için semantic-transparent style-fit cache eklendi:
  C071C **8295**, B996A **5911**, C004A **3099**, C069A **10160** cache hit. Reverse traversal
  bilerek ayrı fit ediliyor; cache çizim semantics'ini değiştirmiyor.
- Son four-design Action green: roundtrip exact %94.76 / %94.64 / %95.94 / %94.62; ±1px
  %99.93 / %99.87 / %99.95 / %99.90; palette SAFE; C004A exact TB symmetry korunuyor.
- Normal motif `RugScale` execution path'i değişmedi.


### Leaf / Petal Arcs — estetik dış-yay motoru (2026-09-24)

- Kullanıcının B996 800×1320 ekran görüntüsünde fill taşması çözülmüş olmasına rağmen uzun yaprak
  yaylarının source/manuel çizim kadar akıcı olmadığı doğrulandı. Sorun Curve & Fill'e global
  smoothing eklenerek değil, ayrı `ScaleMode.LeafPetalArcs` ile ele alındı.
- UI'a **RugScale Leaf / Petal Arcs — elegant tapered floral curves** seçeneği eklendi.
- `LeafPetalLobeExtractor`: ortak stem'e bağlı aynı renk yaprakları source skeleton endpoint
  branch'lerinden ayrı lobe'lara ayırıyor.
- `LeafPetalBoundaryCurveBuilder`: protected outline renginden yaprağın iki gerçek dış tarafını
  source raster üstünde topluyor. İç beyaz slit'in outer curve sanılmaması için side-sign + real
  outline adjacency + directional source-graph traversal eklendi.
- `LeafPetalBoundaryCurveFitter`: leaf side'ı RugCAD `SplineThroughPoints` olarak source-first
  fit ediyor; exact/near raster, endpoint tangent, full macro tangent profile, curvature flip ve
  control complexity birlikte puanlanıyor. 4–5 kontrol noktası tercih; 6–7 ancak source açıkça
  gerektiriyorsa kazanabiliyor.
- `LeafPetalBoundaryPairOptimizer`: iki dış kenarın target roundness'ını **birlikte** optimize
  ediyor. Source controls değişmiyor. Full standard roundness grid'i aranıyor; source support,
  tangent flow, turn/curvature profile, width-profile retention ve width smoothness birlikte
  skorlanıyor.
- `LeafPetalBoundaryCurveRasterizer`: yalnız outer source corridor içinde işlem yapıyor. Inner
  slit/dekoratif outline aynı beyaz palette index'inde olsa bile source recovered outer-path'e
  1.2px yakın değilse silinmiyor.
- Yeni testler:
  - paired designer curves target-scale ±1px geometri,
  - inner slit retention,
  - joint optimizer mismatched roundness regression,
  - mevcut separator/fill-bleed korumaları.
- B996 audit CSV'si artık left/right source path length, control count, source roundness,
  800×1320 optimized roundness ve `TargetPairAdjusted` alanlarını raporluyor.
- GitHub Actions şu anda runner katmanında bloke: son run + failed-job rerun attempt'lerinde
  `runner_id=0`, `steps=[]`; hiçbir checkout/restore/test adımı başlamadı. Bu nedenle yeni
  commit'ler CI-green ilan edilmedi. `main` değişmedi; tüm çalışma
  `chatgpt/rugcad-work-2026-09-22` branch'inde.
