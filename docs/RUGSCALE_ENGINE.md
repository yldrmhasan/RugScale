# RugScale — Indexed Carpet Design Resampling

**Durum:** aktif geliştirme / üretim adayı  
**Son güncelleme:** 23 Eylül 2026  
**Core:** `RugScaleEngine.cs` + `MotifShrinkEngine.cs` + `MotifMemory.cs` + `MotifRepairEngine.cs` + `MotifSourceCatalog.cs`  
**Dispatcher:** `src/RugScale.Core/Drawing/DesignResizer.cs`  
**Host:** UI bağımsız; manuel çalışma için `tools/RugScale.Cli`, entegrasyon için `RugScale.Core` API  
**Testler:** `tests/RugScale.Core.Tests/DesignResizerTests.cs` + `MotifMemoryTests.cs`

## Amaç

RugScale fotoğraf küçültme algoritması değildir. Indexed-color halı desenlerini farklı üretim
ebatlarına taşırken gerçek desen çizicisinin yaptığı yapısal kararları korumayı hedefler.

Klasik nearest / bilinear / area-average yalnız örnekleme yapar. Halı desen çiziminde ise:

- ince dal tamamen kaybolmamalı,
- köşe ve endpoint okunur kalmalı,
- T/X junction source'ta yoksa sonradan oluşmamalı,
- ayrı motifler resize sırasında yanlış köprüyle birleşmemeli,
- raport/repeat fazı bozulmamalı,
- gerçek tasarım simetrisi grid fazı yüzünden kaybolmamalı,
- küçük ebat büyük ebadın kör piksel kopyası değil, yeniden dengelenmiş motif olmalıdır.

RugScale bütün kararları palette-index seviyesinde verir ve source'ta olmayan yeni RGB renk üretmez.

---

## Kalite öncelikleri

RugScale'ın kalite sırası kabaca şöyledir:

1. palette/index güvenliği,
2. repeat/rapport faz bütünlüğü,
3. designer symmetry,
4. junction / endpoint / thin-branch bağlantısı,
5. bağımsız motiflerin yanlış birleşmemesi,
6. corner / contour devamlılığı,
7. region ve renk alan dengesinin makul kalması,
8. source-centre / nearest benzerliği.

Bu nedenle nearest baseline'a en az piksel farkı kalite hedefi değildir.

---

## Progressive ve heavy shrink

Gerçek üretim akışı çoğu zaman:

```text
200x300 -> 160x230 -> 120x180
```

şeklindedir.

### Progressive shrink

Minimum eksen ölçeği yaklaşık `>= 0.72` olduğunda source çizime yakın, hızlı yol kullanılır.

Başlıca katmanlar:

- source-centre baseline,
- exact candidate footprints,
- branch segment repair,
- multi-zone repeat stabilization,
- unsupported merge pruning,
- **V7 motif-atlas reconstruction**,
- phase-aware hard symmetry.

Progressive yolda pahalı full-region BFS gereksizse atlanır.

### Heavy shrink

Daha sert küçültmede:

- küçük region kaybı,
- area drift,
- connectivity kaybı

için daha agresif structural / region düzeltmeleri uygulanır.

---

## Source feature analizi

Her source piksel categorical indexed raster olarak analiz edilir.

Tutulan başlıca bilgiler:

- palette index,
- 8-neighbour same-color count,
- edge / contour,
- thin-line,
- corner,
- endpoint,
- junction,
- importance score.

Kritik yapı önceliği:

```text
junction
> endpoint / sharp corner
> thin skeleton / branch
> normal contour
> small region
> interior
```

---

## Candidate footprint sistemi

Her target hücrenin source footprint'i hesaplanır.

Kavramsal sınırlar:

```text
x0 = tx * srcW / dstW
x1 = (tx + 1) * srcW / dstW
y0 = ty * srcH / dstH
y1 = (ty + 1) * srcH / dstH
```

CandidateSet şu evidence'ları birleştirir:

- footprint coverage,
- palette index desteği,
- structural importance,
- source-centre rengi,
- kritik feature desteği.

Çıktıda source palette dışında renk oluşmaz.

---


## V7 — Motif Atlas Shrink

V7 ile progressive downscale yalnız target piksel seçmekten çıkarılıp **source motiflerini hatırlayıp
yeni ebatta yeniden kurma** yaklaşımına taşındı.

Ana pipeline:

```text
source indexed design
    -> connected motif primitives
    -> colour-independent shape families
    -> target motif dimensions / positions
    -> reusable target masks
    -> each occurrence's own palette colour
    -> repeat / topology / symmetry reconciliation
```

Bu katman şimdilik yalnız **küçültmede** çalışır. Hedef boyut source'tan küçük değilse motif atlası
devreye girmez. Motif-aware enlargement ileride ayrı bir mod/pipeline olacaktır.

### Motif primitive

Motif hafızasının ilk atomik birimi aynı palette indexe ait **8-connected component**'tir. Her
component için palette index, alan, bounding box, merkez/yerleşim geometrisi, boundary yoğunluğu,
çevresindeki baskın renk ve structural/decorative sınıfı tutulur.

Çok renkli bir motif tek dev binary obje haline zorlanmaz. Onu oluşturan renk parçaları ayrı
primitive olarak korunur; source merkezleri aynı global transform ile target'a taşındığı için
çok renkli motif içindeki göreli yerleşim de korunur.

### Structural backbone ayrımı

Her connected component yeniden çizilmez. Tam scan-line/sentinel/seam, çok büyük field/background
bölgeleri ve document ekseninin çok büyük kısmını kaplayan frame/border gövdeleri mevcut
repeat/region motoruna bırakılır. Küçük ve orta dekoratif parçalar — tek piksellik detay dahil —
motif atlasına alınır.

### Shape family — renk geometriden ayrıdır

Aynı şeklin farklı renklerde tekrar etmesi ayrı geometri problemi değildir. Family signature:

- normalize edilmiş **16×16 binary occupancy**,
- aspect-ratio bucket,
- fill-ratio bucket

bilgilerinden oluşur; palette index family key'e dahil edilmez.

Örneğin aynı yaprağın lacivert, kahve ve krem occurrence'ları tek shape family olabilir. Source
extentleri yeterince yakınsa target maskesi yalnız bir kez üretilip cache'lenir; her occurrence aynı
maskeyi kendi palette indexiyle boyar. Böylece farklı renk varyantları farklı şekilde deforme olmaz.

### Target motif ebadı

Başlangıç hedefi:

```text
targetW ~= round(sourceW * scaleX)
targetH ~= round(sourceH * scaleY)
```

Çok küçük veya çizgi-benzeri motif normal rounding ile tek hücreye çökecekse bounded minimum-detail
kuralı uygulanabilir. Motif kaybolmaya direnebilir, fakat source'a göre göreceli olarak büyütülmez.

### Forward projection

Target maskesi inverse-nearest ile örneklenmez. Source motif piksellerinin merkezleri target motif
maskesine **forward-project** edilir. Bu sayede 8-connected bir source primitive, yalnız target
sample noktasına denk gelmediği için rastgele kopmaz. Non-empty source motif en az bir target hücre
ile temsil edilir.

### Baseline kalıntılarını temizleme

Centre-nearest baseline motifin eski parçalarını farklı fazda bırakabilir. Atlas, mapped source
primitive'e ait baseline kalıntısını önce primitive'in source'taki baskın çevre rengine temizler;
sonra target motif maskesini çizer. Böylece eski sampling parçası ile yeni motif üst üste gelip
hayalet kol, kalınlaşma veya sahte junction üretmez.

### Motif çakışma önceliği

Target'ta iki motif primitive aynı hücreye düşerse küçük motif, yüksek boundary/detail oranlı motif
ve daha seyrek palette index daha yüksek öncelik alır. Amaç küçük yaprak ucu veya ince dekoratif
parçanın büyük dolgu altında kaybolmasını önlemektir.

### Repeat ile öncelik ilişkisi

Progressive sıranın özeti:

```text
local branch/topology repair
    -> repeat stabilization
    -> unsupported-merge pruning
    -> motif atlas
    -> hard designer symmetry
```

V5 outer-repeat pass'inin **gerçekten değiştirdiği** target hücreler önce/sonra snapshot farkından
çıkarılır ve motif atlasından korunur. Böylece bütün border kilitlenmez; yalnız canonical repeat'in
gerçekten düzelttiği hücreler korunur. Tek bozuk occurrence yeniden ortaya çıkmazken repeat dışı
motifler source geometrisine göre yeniden kurulabilir.

Küçük rasterlarda outer-repeat detector'ın bütün resmi border sanmaması için cross-axis minimumu
64 px'tir; küçük rasterları motif/topology yolu yönetir.

### Gerçek desenlerde family reuse

B054A ve B163A source analizinde on binlerce dekoratif connected primitive ve çok sayıda tekrar eden
shape family bulundu. Aynı normalized geometrinin farklı palette indexlerle tekrar ettiği çok
sayıda occurrence olduğu için shape/color ayrımı gerçek üretim verisine karşılık gelir.

B054A/B163A adı, koordinatı veya palette indexi algoritmada hard-code edilmez; hard-code edilen
şey motif family bulma ve yeniden çizim prensibidir.

---

## V8 — Öğrenen motif hafızası ve kullanıcı doğrulaması

V8 ile RugScale yalnız otomatik shrink sonucu üreten bir resizer olmaktan çıkarılıp, kullanıcının
bozuk gördüğü motifleri source tasarımdan yeniden bulup öneren ve kabul/red geri bildirimiyle kalıcı
bilgi biriktiren bir **motif learning / repair loop** kazandı.

Bu sistem black-box bir görüntü modeli değildir. İlk katman deterministic ve indexed-raster
semantiklerine bağlıdır; öğrenilen bilgi taşınabilir JSON olarak saklanır ve sürümden bağımsız
olarak paylaşılabilir.

### Taşınabilir motif memory

Runtime hafıza:

```text
%AppData%\RugScale host application\rugscale-motif-memory.json
```

Dosya Import/Export edilebilir. Böylece kullanıcı güncel motif hafızasını dışarı verebilir; aynı
dosya yeni family/seed bilgileri eklenip tekrar uygulamaya merge edilebilir.

Memory family palette index'e bağlı değildir. Descriptor:

- normalize edilmiş 24×24 color-role grid,
- structural edge grid,
- fiziksel Warp/Weft aspect,
- rotate/mirror invariant canonical form,
- pozitif / negatif kullanıcı feedback sayıları,
- onaylı source sample bilgileri

taşır.

Aynı yaprak başka palette indexlerle boyanmışsa yeni family oluşmak zorunda değildir. Rotate 90/180/
270 ve mirror varyantları da aynı family altında tanınabilir.

### Warp / Weft ve yön

Motif eşleşmesi yalnız pixel width/height oranına göre yapılmaz. Source ve target kendi Warp/Weft
değerleriyle fiziksel aspect üretir.

Family tanıma rotate/mirror invariant'tır; repair render aşamasında ise source occurrence'ın target
instance'a hangi rotate/mirror dönüşümüyle oturacağı yeniden bulunur. Böylece family aynı kalırken
instance yönü korunur.

### Renk rolü — geometri renkten ayrıdır

Source'ta bulunan motif başka renk varyantındaysa source palette indeksleri target'a kör kopyalanmaz.

Motor source color-role ↔ target color-role eşleşmesini çıkarır ve source geometrisini target
instance'ın mevcut renkleriyle yeniden rasterize eder. Distinct motif rolleri mümkün olduğunca
one-to-one tutulur; leaf fill ve vein gibi iki rol tek renge yanlış collapse edilmez.

### Hierarchical source motif catalogue

Interactive arama öncesi source design için session boyunca cache edilen bir motif catalogue
oluşturulur.

Catalogue sınıfları:

- `Branch`: dal / çizgi / ince uzun structure,
- `LeafLike`: kompakt küçük motif parçası,
- `Primitive`: tek connected dekoratif parça,
- `LargePart`: büyük motifin anlamlı component'i,
- `Compound`: birbirine yakın farklı renk/componentlerden oluşan çok renkli motif.

Tek piksel kalınlığında ama uzun component'ler fill ratio yüksek olsa bile `Branch` sayılır.
Background/frame/sentinel gibi structural backbone componentleri katalogdan çıkarılır.

Matcher önce exact catalog entries üzerinde çalışır. Katalog segmentation'ının yetersiz kaldığı
durumlarda bounded rectangular sliding-window arama fallback olarak korunur.

### Candidate sıralaması: source konumu authoritative evidence

Interactive repair normal bir "benzer resim bul" problemi değildir. Target'ta seçilen alanın
original source içindeki normalize karşılığı bilinir. Aynı veya çok benzeyen yaprak/rozet/compound
motif source'un başka yerlerinde tekrar etse bile **ilk aday** inverse-mapped source çevresinden
gelmelidir; uzak occurrence'lar ancak geometri kanıtı gerçekten daha güçlüyse veya kullanıcı
`Different source` ile alternatif istediğinde öne çıkmalıdır.

23 Eylül düzeltmesiyle candidate skoru descriptor + relative transform + motif mass + source-position
prior'ını birlikte kullanır. Ayrıca local ve global rectangular fallback ayrıldı. **Inverse-mapped
local fallback her detect işleminde mutlaka score edilir**; güçlü exact catalogue eşleşmesi yalnız
pahalı global fallback taramasını suppress edebilir. Catalogue exact-mask ile local rectangular
fallback aynı bounding box'a sahip olsa bile ayrı hypothesis olarak değerlendirilir; biri diğerini
`seen` dedup ile susturamaz. `Compound` etiketi de büyük bonus değildir; yalnız küçük bir tie-break
olarak kullanılır. Bu özellikle birbirine yakın birden fazla component'in yanlışlıkla tek "büyük
yüz/rozet" compound'u gibi gruplanıp gerçek motifin önüne geçmesini engeller.

### Resize / RugScale feedback akışı

Motif feedback yalnız **RugScale + shrink** durumunda açılır.

Sağ preview gerçek `DesignCanvas`'tır. Kullanıcı:

1. `Click part` ile bağlı bir motif parçasını,
2. `Polygon motif` ile mevcut point-by-point polygon selection aracını

kullanarak bozuk bölgeyi seçer.

Motor source design içinde adayları arar, en güçlü adayı target ebatta preview'a uygular ve candidate
score/source position gösterir.

- **✓ Good / learn**: öneri preview zincirine sabitlenir ve family pozitif feedback alır.
- **↻ Bad / next**: mevcut aday reddedilir, family confidence düşer ve sonraki source adayı gösterilir.
- Pending öneri hafızaya yazılmaz.
- OK basılana kadar gerçek document değişmez.
- Birden fazla kabul edilen repair aynı non-destructive preview üzerinde birikir.

Learned family confidence sonraki aramalarda bounded bir tie-break olarak kullanılır; source geometry
kanıtının yerine geçmez.

### Seed memory

İlk paketlenmiş motif hafızası gerçek referanslardan çıkarılmış descriptor family'leriyle başlar.
Özellikle B054A'nın `_0014` referans ailesi ve B163A original source başlangıç knowledge tabanına
seed sağlar.

Seed raw carpet bitmap içermez; normalize motif descriptor/sample bilgisi taşır. Kullanıcının
%AppData% memory'si varsa packaged seed onun üstüne merge edilir ve kullanıcı feedback'i
silinmez/override edilmez.

### Preview navigation kontratı

Feedback sırasında candidate değiştirmek navigasyonu değiştiremez.

`DesignCanvas.ViewportState` şu bilgiyi saklar:

- zoom,
- viewport merkezinin normalized design X koordinatı,
- viewport merkezinin normalized design Y koordinatı.

Candidate gösterme, Good/Bad, accepted repair recomposition veya preview document swap sonrası aynı
view state geri kurulur. Aynı ebatta kullanıcı aynı ekran alanında kalır; ebat değişirse aynı göreli
motif bölgesi korunur.

RugScale preview navigasyonu workspace ile aynı `DesignCanvas` davranışını kullanır:

- Ctrl+mouse-wheel zoom,
- normal wheel dikey pan,
- Shift+wheel yatay pan,
- Pan tool / Alt+Space / middle mouse davranışları,
- kullanıcı tarafından atanmış `pan.left/right/up/down`,
- kullanıcı tarafından atanmış `view.zoomIn/zoomOut`,
- varsa Normal Size / Fit / Center / Restore View atamaları.

Klavye atamaları ayrı RugScale tablosundan gelmez; mevcut `AppSettings.Shortcuts` kullanılır.
Polygon/selection aktifken Enter/Escape/Backspace/Shift+arrow önce normal
`DesignCanvas.HandleDrawingKey` akışına gider, ancak araç tüketmezse pan/zoom shortcut çalışır.

---

## Thin branch graph

İnce motifleri yalnız global rarity ile korumak yeterli değildir; noise da yanlışlıkla korunabilir.

RugScale source thin-line piksellerini same-color graph olarak işler.

### Node

Same-color 8-neighbour graph'ta degree `!= 2` olan pikseller node kabul edilir:

- endpoint,
- T junction,
- X junction,
- branch corner.

### BranchSegment

İki node arasındaki degree=2 yol branch segment olarak saklanır.

Closed contour loop'lar branch listesine alınmaz; böylece dolu motif kenarları yanlışlıkla ince dal
gibi tekrar çizilmez.

### Target projection

Branch source'dan target'a centre mapping ile projekte edilir.

- duplicate target hücreleri düşürülür,
- source candidate desteği olan eksik centreline hücreleri onarılır,
- iki yaşayan branch hücresi arasındaki tek-pixel hole gerektiğinde doldurulur.

---

## Repeat / rapport motoru

### Outer repeat

Üst / alt / sol / sağ borderlarda:

- fundamental period,
- repeat span,
- repeat count,
- phase,
- confidence

hesaplanır.

Karşılıklı simetrik borderlar aynı period ailesine harmonize edilir.

### Canonical tile

Tek bir occurrence körlemesine kopyalanmaz.

Aynı phase/cross hücresi bütün occurrence'larda sayılır ve majority canonical tile üretilir.

Örnek:

```text
10 tekrar
9 doğru
1 bozuk
```

ise bozuk occurrence bütün bordüre yayılmaz.

### Repeat authority

Güçlü repeat zone'da yaklaşık:

```text
band score >= 0.80
consensus >= 0.72
```

olduğunda raport kanıtı lokal branch/keypoint kararından daha güçlü olabilir.

### Inner repeat — V6

Repeat artık yalnız dış kenardan başlamak zorunda değildir.

Detector:

- full-axis,
- first-half,
- second-half,
- central windows

üzerinde period probe üretir.

Böylece:

- inner horizontal border,
- inner vertical border,
- medallion/field tarafından kesilmiş repeat strip

ayrı zone olarak yakalanabilir.

### Zone arbitration

- Specialized outer detector inner detector tarafından override edilemez.
- Büyük ölçüde çakışan farklı inner period hipotezleri birlikte repaint yapamaz.
- Stabilization confidence sırasıyla yürür.

---

## Grid-phase-aware designer symmetry

Gerçek halı rasterında motif geometrik simetrik olduğu halde klasik:

```text
x <-> width - 1 - x
```

testi 1–2 px grid fazı yüzünden başarısız olabilir.

RugScale ±3 px mirror phase arar.

Hard mirror yalnız çok güçlü evidence varsa etkinleşir:

```text
pixel agreement      >= 0.992
structural agreement >= 0.985
structural coverage  >= 0.20
```

Structural coverage koşulu küçük/sparse bir T şeklinin bütün document'i yanlışlıkla symmetry
constraint'e sokmasını engeller.

### Target re-centering

Source'taki faz farkı target'a aynen taşınmaz. Küçük ebat gerçek desen çizicisinin yaptığı gibi
geometrik merkeze yeniden oturtulur.

### Technical sentinel / seam

Indexed BMP'de bir kenarda uniform ve globally rare teknik 1 px scan-line varsa symmetry content
alanının dışında tutulabilir.

### Symmetric orbit voting

Mirror orbitteki 2/4 target hücrenin rengi:

- source CandidateSet,
- current target vote,
- locked structural evidence,
- ortak palette desteği

ile seçilir. Tek bozuk quadrant körlemesine çoğaltılmaz.

---

## Unsupported topology merge guard

Farklı gerçek desenlerde bazı palette indexlerin resize sonrası source'ta olmayan büyük connected
componentlere dönüştüğü görüldü.

Repeat zone dışında bir target piksel aynı rengin 3+ ayrı neighbour arc'ını birleştiriyorsa yeni
T/X junction olarak değerlendirilir.

Mapped source footprint'te gerçek junction desteği yoksa ve source-centre alternatifi yeterli
candidate desteğine sahipse bu köprü geri alınır.

Verified repeat canonical zone bu filtreden muaftır.

---

## Gerçek referanslardan çıkan kurallar

### B054A

200x300 kaynak ve insan çizilmiş 160x230 referans karşılaştırıldı.

Bulgular:

- insan referans nearest resize değildir,
- source ±1/±2 px grid phase ile gerçek designer symmetry taşır,
- insan çizilmiş küçük ebat bu symmetry'yi korur,
- eski RugScale sürümleri symmetry'yi ciddi bozabiliyordu,
- bazı thin floral branchler gereğinden fazla sadeleşebiliyordu.

Bu analiz hard symmetry + branch/topology kurallarını doğurdu.

### B163A

Farklı desen ailesinde:

- RugScale edge density açısından nearest baseline'a yakındı,
- fakat bazı palette componentleri source'ta olmayan bridge/junction ile birleşiyordu.

Bu analiz unsupported merge pruning kuralını doğurdu.

### Genel kural

B054A/B163A koordinatları, özel palette indexleri veya isimleri algoritmada hard-code edilmez.

Hard-code edilenler **desen çizim prensipleridir**:

- raport aynı fazda tekrar eder,
- gerçek simetri resize ile bozulmaz,
- ayrı motif source kanıtı olmadan birleşmez,
- endpoint/junction/topology dekoratif çoğunluk oyundan daha önemlidir.

---

## Enlargement

V7 motif atlası **yalnız shrink** için etkinleştirilmiştir.

Saf enlargement'ta mevcut davranış korunur; RugScale kategori-nearest fallback kullanır ve
EdgeSmooth/EPX ayrı seçenektir. Motif-aware enlargement ileride aynı atlas fikrini paylaşabilir,
fakat target-detail üretme kuralları shrink'ten ayrı olacaktır.

---

## Palette güvenliği

RugScale host application document categorical/indexed rasterdır.

RugScale:

- yeni RGB renk oluşturmaz,
- source palette index semantics'ini korur,
- result `DesignDocument` palette uyumluluğunu korur,
- Stop/Park aktarımını ortak `ReplaceDocument` akışına bırakır.

---

## Undo ve commit

Resize sonucu önce ayrı bir `DesignDocument` olarak üretilir.

Gerçek belgeye commit:

```text
DesignSurfaceViewModel.ReplaceDocument(...)
```

üzerinden yapılır.

Bu tek mantıksal Undo/Redo kaydı üretir.

---

## Resize penceresi ve canlı preview

Resize artık maximized workspace olarak açılır.

Sol panel:

- Pixels X / Y,
- Warp / Weft,
- Keep Aspect Ratio,
- Scale Image,
- resize mode,
- anchor / distribution,
- fill palette index.

Sağ panel:

- non-destructive gerçek sonuç preview,
- fiziksel Warp/Weft pixel aspect,
- hesaplama durumu,
- hedef size / mode bilgisi.

### Preview davranışı

Ayar değişiklikleri yaklaşık 300 ms debounce edilir.

RugScale hesaplaması UI thread dışında yürür. Eski preview tamamlanırken input değişirse stale sonuç
ekrana basılmaz; son ayarlar yeniden hesaplanır.

OK yalnız güncel preview hazırsa etkinleşir.

En önemli kontrat:

> OK basıldığında RugScale ikinci kez çalıştırılmaz. Kullanıcının gördüğü exact preview
> `DesignDocument` tek `ReplaceDocument` işlemiyle belgeye uygulanır.

Cancel hiçbir document değişikliği yapmaz.

### RugScale motif feedback workflow

RugScale shrink preview artık yalnız algoritmanın sonuç gösterdiği pasif bir ekran değildir; kullanıcı
tarafından doğrulanan motif-repair workspace'idir.

İşlem sırası bilerek iki aşamaya ayrılmıştır:

1. **Source selection**
   - `Rect select` normal rectangular selection kullanır.
   - `Polygon select` gerçek `PolygonSelection` aracını kullanır; çizim `Polygon` aracı değildir.
   - `+ Grow` mevcut selection'ı 1 px genişletir.
   - `- Contract` mevcut selection'ı 1 px daraltır.
   - selection değişmesi kendi başına hiçbir motif araması veya repair başlatmaz.
2. **Detect motif**
   - yalnız kullanıcı `Detect motif` düğmesine bastığında source motif kataloğu ve motif memory
     kullanılarak adaylar aranır.
   - main resize preview değişmez.
   - bulunan source crop sağdaki açılır/kapanır `Source motif` panelinde nearest-neighbor görüntülenir.
   - Previous / Next aday gezdirme learning yapmaz.
3. **Target area**
   - varsayılan hedef source selection'ın resized preview'daki mevcut alanıdır.
   - `Target rect` veya `Target polygon` ile motifin yeni ebatta oturacağı alan source detection
     alanından bağımsız seçilebilir.
   - `Use detected area` source selection alanını tekrar hedef yapar.
4. **Preview repair**
   - detected source motif seçilen target width/height/mask'e tekrar rasterize edilir.
   - source geometry / rotate / mirror bilgisi korunur.
   - source'tan öğrenilmiş color-role -> target-instance color-role eşlemesi korunur.
   - bu aşama hâlâ non-destructive preview'dır.
5. **Feedback**
   - `✓ Good / learn`: preview repair accepted zincirine girer, motif family'ye pozitif sample
     yazar ve kullanılan `MotifRepairStyle` için başarılı redraw feedback'i kaydeder.
   - `↻ Bad / improve`: **source candidate değiştirmez**. Aynı source X/Y/W/H, transform,
     color-role map ve aynı target alan korunur; yalnız redraw stratejisi değiştirilerek motif
     yeniden rasterize edilir.
   - redraw sırası motif türüne göre değişir. Kullanılan stratejiler:
     `Balanced`, `PreserveBranches`, `ContourFirst`, `ConnectivityFirst`,
     `DetailAndConnectivity`.
   - Bad feedback motif family'nin source-match confidence'ını düşürmez; yalnız başarısız redraw
     stilini family hafızasında negatifler.
   - `Different source`: yalnız source motifin kendisi yanlışsa kullanılır. Bu durumda current
     source family negatif feedback alır ve başka source candidate'a geçilir.
   - accepted repair'ler Resize `OK` basılana kadar gerçek document'e yazılmaz.
   - motif family daha önce öğrenildiyse, başarılı redraw stilleri sonraki aynı/benzer motiflerde
     daha erken denenir; sık başarısız olan stiller daha sona kayar.

### Source ve target alanlarının ayrılması

Shrink'te source motif bounding box ile target motif bounding box'ın aynı olması beklenmez.

Örnek:

```text
source motif : 40 x 60
detected preview area : 31 x 46
user target area : 29 x 44
```

Motor source motifin geometry/transform/color-role bilgisini saklar ve `Preview repair` sırasında
29x44 target mask için yeniden rasterize eder. Böylece motif yalnız detection rectangle'a sıkıştırılmaz.

### Selection / context-menu kontratı

Resize/RugScale embedded `DesignCanvas` normal editor selection motorunu kullanır, fakat normal
document sağ-tık selection context menu'sü bu workspace'te bilinçli olarak kapalıdır.

Bu değişiklik yalnız embedded Resize canvas için geçerlidir; ana Workspace `DesignCanvas` sağ-tık
davranışı değişmez.

Motif selection araçları:

- Rectangle Selection,
- Polygon Selection,
- Grow +1,
- Contract -1,
- Add area (bir sonraki rectangle/polygon mevcut selection'a eklenir),
- Subtract area (bir sonraki rectangle/polygon mevcut selection'dan çıkarılır),
- custom target Rectangle,
- custom target Polygon.

### View persistence

Motif detection / candidate navigation / target selection / Preview Repair / Good / Bad sırasında:

- zoom sıfırlanmaz,
- pan sıfırlanmaz,
- viewport center sıfırlanmaz,
- candidate değiştirmek kullanıcıyı başka bir motif bölgesine atmaz.

Geçici candidate preview kaldırılırken canvas accepted/base preview'a döner ve aynı
`DesignCanvas.ViewportState` geri yüklenir.

### Hızlı input

Resize'deki numeric inputların tamamı:

- Tab,
- Shift+Tab,
- ilk mouse click

ile fokus aldığında mevcut değerin tamamını seçer. Kullanıcı Ctrl+A yapmadan yeni değeri doğrudan
yazabilir.

---

## Advanced Color Picker ile ilgili not

RugScale'a doğrudan bağlı değildir fakat aynı UI paketinde picker canlı preview davranışı düzeltildi.

- picker rengi arka plandaki gerçek desende anlık görünür,
- preview Undo stack'e yazılmaz,
- Cancel/ESC orijinal palette rengini geri getirir,
- OK orijinal -> final renk için yalnız bir `PaletteColorCommand` üretir,
- mouse drag preview yaklaşık 24 ms coalesce edilir,
- field bitmap yalnız Hue değiştiğinde,
- wheel bitmap yalnız Value değiştiğinde yeniden render edilir.

---

## Test kontratları

Core regression setinde bulunan RugScale senaryolarının başlıcaları:

- source'ta olmayan palette index oluşmaması,
- source document'in mutate edilmemesi,
- uniform raster,
- enlargement fallback,
- one-pixel contour preservation,
- connected L corner,
- endpoint/junction continuity,
- progressive branch graph,
- horizontal outer repeat,
- vertical outer repeat,
- majority canonical repeat,
- inner horizontal repeat zone,
- inner vertical repeat zone,
- phase-shifted left/right designer symmetry,
- phase-shifted top/bottom re-centering,
- technical sentinel handling,
- independent arms'ın unsupported junction ile birleşmemesi,
- realistic large-raster stack/performance regressions,
- aynı geometri / farklı palette index motif-family reuse,
- ayrı tiny motiflerin shrink sırasında kaybolmaması veya birleşmemesi.

Son tam Windows doğrulamasında **167/167 Core testi** ve **83/83 App regression kontrolü** geçti.
RugScale/MotifMemory/MotifAtlas odaklı filtre ayrıca **30/30** geçti.

---

## Performans notları

Geçmişte gerçek 960x2250 rasterda branch graph yüzünden 3+ dakika görülen sürüm vardı.

Başlıca optimizasyonlar:

- HashSet-edge bookkeeping yerine adjacency/visited bit masks,
- cached neighbour counts,
- sparse branch graph,
- bounded period search,
- harmonic adaylarının sınırlı denenmesi,
- progressive yolda full region analizinin atlanması,
- preview render için maksimum bitmap boyutu sınırı.

V7 motif-atlaslı deterministic Release CI benchmarkları:

```text
960x2250 -> 768x1725 : ~3127 ms
960x1500 -> 768x1150 : ~1129 ms
```

İlk oran B054A, ikinci oran B163A üretim boyutlarına yakındır. Bunlar gerçek dosyaların birebir
benchmarkı veya Windows workstation SLA'sı değildir; motif atlasının on binlerce component
kataloglamasına rağmen dakika seviyesinde regresyon yaratmadığını doğrulayan kontrollü
karşılaştırmalardır.

---

## Geliştirme kuralları

RugScale değiştirirken:

1. Tek gerçek desene özel koordinat / palette index patch'i ekleme.
2. Yeni kuralı halı çizim prensibi olarak tanımla.
3. Mümkünse karşı örnek regression testi ekle.
4. Existing repeat / branch / symmetry kontratını kırma.
5. Indexed palette dışında renk üretme.
6. Progressive performansı O(N)-ağırlıklı tut.
7. Preview ile commit sonucunun birebir aynı kalmasını koru.
8. UI değişikliğinde WPF build + App regression testini de çalıştır.

---

## İlgili dosyalar

```text
src/RugScale.Core/Drawing/DesignResizer.cs
src/RugScale.Core/Drawing/RugScale/RugScaleEngine.cs
src/RugScale.Core/Drawing/RugScale/MotifShrinkEngine.cs
src/RugScale.Core/Drawing/RugScale/MotifMemory.cs
src/RugScale.Core/Drawing/RugScale/MotifRepairEngine.cs
src/RugScale.Core/Drawing/RugScale/MotifSourceCatalog.cs

src/RugScale host application.App/Services/MotifMemoryStore.cs
src/RugScale host application.App/Windows/ResizeDesignWindow.xaml
src/RugScale host application.App/Windows/ResizeDesignWindow.xaml.cs
src/RugScale host application.App/ViewModels/DesignSurfaceViewModel.cs

tests/RugScale.Core.Tests/DesignResizerTests.cs
tests/RugScale.Core.Tests/MotifMemoryTests.cs
docs/05-devam-notu-durum.md
```

Bu dosya RugScale için toplu teknik kaynak olarak tutulmalıdır. Kronolojik geliştirme günlüğü
`docs/05-devam-notu-durum.md` içinde kalır.


## Motif repair — isolated source mask contract

Motif feedback/repair no longer treats a rectangular source crop as the motif.

- Every repair candidate carries an explicit `SourceMask`.
- Catalogue candidates use the exact component/compound mask from `MotifSourceCatalog`.
- Fallback rectangular search derives a foreground mask by identifying true crop-dominant perimeter/background colours; enclosed field holes are excluded as background too.
- Source sidebar rendering is transparent outside `SourceMask`; the user sees only the detected motif, not its surrounding field/border crop.
- Colour-role matching is computed only from masked source motif pixels.
- A target field/background colour is not accepted as a motif colour mapping merely because a broken target instance contains a large empty/field area.
- `RetargetCandidate` always re-rasterizes from the original `DesignDocument + SourceMask`. A failed redraw is never used as the input for the next redraw.
- Rasterization produces a separate `TargetMask`. `ApplyCandidate` writes only pixels in this generated motif mask; target pixels outside it remain untouched even when the user selected a rectangular target area.
- Every meaningful source palette role represented in the isolated source motif gets a survival opportunity during shrink, including rare colours and thin branch/detail pixels.

This contract is intentional: the repair loop may change sampling/contour/connectivity strategy, target box, rotation/mirror transform, or colour-role mapping, but it may not invent motif evidence from surrounding crop background.


### Source-first repair hardening — 2026-09-23

The interactive repair path was hardened after real-design feedback showed two failure modes: rectangular source crops were leaking field/background colours into the repaired motif, and small source fragments could outrank the complete motif.

Current contract:

- **Target selection is a search/placement area, not automatically the motif.** RugScale derives a target foreground motif mask first and excludes dominant field/background roles from descriptor matching.
- **Source candidates are motif masks.** Hierarchical `MotifSourceCatalog` candidates use their exact component/compound mask. Rectangular scanning is now last-resort fallback only.
- Fallback descriptors are built from the same inferred foreground mask that will actually render; a rectangular crop can no longer score using background pixels and then render only a tiny residue.
- Multi-role target motifs reject source candidates that cannot represent the same meaningful colour-role count. This prevents a one-colour leaf/vein fragment from replacing a multi-colour whole motif.
- Candidate ranking includes **resize-aware motif mass preservation**. Expected source motif pixel mass is derived from the global source→target area ratio; heavily clipped candidates are penalized.
- Colour-role mapping uses only source-mask pixels and treats dominant target field colours as ineligible motif roles. Remaining motif colours are paired one-to-one by strong overlap / role evidence.
- Every `Bad / improve` redraw starts from the immutable original `DesignDocument + SourceMask`; a failed redraw is never the input of the next attempt.
- Generated repair output has its own `TargetMask`. Applying a repair writes only motif-mask pixels, leaving unrelated target/background pixels untouched.
- The source motif sidebar renders pixels outside `SourceMask` transparent, so the inspector shows the detected motif rather than a rectangular crop.
- The preview keeps the same workspace `DesignCanvas` navigation behavior. Zoom/pan/view centre are preserved while candidate previews are swapped, and configurable workspace pan/zoom keyboard shortcuts remain the source of truth.

Validation after this hardening:
- filtered motif/RugScale Core suite: **33/33 passed**;
- Windows Release solution build: **passed**;
- App regression harness: **83/83 checks passed**.


## B163A gerçek-raster GitHub Actions kalibrasyonu — 23 Eylül 2026

Gerçek `CERP-MD2_B163A_PD277_0016_org.bmp` kaynak rasterı repository içinde lossless XZ/base64
fixture olarak saklanıp iki ayrı GitHub Actions doğrulamasında çalıştırılır. Kaynak **960×1500 px**,
kalite **48×50**; aynı kaliteyle **160×230** hedefi **768×1150 px**'dir.

İki doğrulama birbirinin yerine geçmez:

- `RugScale B163A motif audit`: source catalogue'daki bütün motifleri hedef konumlarına projekte
  eder, **40.921 motif** için descriptor alignment + indexed-color agreement + color-role retention
  ölçer; Nearest baseline ile karşılaştırır. Ayrıca full Core regression suite çalışır.
- `RugScale B163A real-design validation`: bütün catalogue entry'lerinde palette/motif retention
  kontrolü yapar, motif türlerine dağıtılmış interactive detector sample'larıyla source ranking'i
  sınar ve whole-design edge/symmetry/palette-area guardrail'lerini uygular.

Son kalibrasyon sonucu:

- Core regression: **172/172 başarılı**.
- Interactive detector: audit sample **16/16 top-1**, real-design sample **20/20 top-1** local source.
- Palette güvenliği: **%100**, yeni palette index yok.
- Bütün motif projection-retention: ortalama **%98,83**, median **%100**, P10 **%98,00**.
- Strict structural audit: ortalama **%72,67**, Nearest'e ortalama delta **+0,1061**;
  **34.164 motif** Nearest'ten >1 puan iyi, **4.094 motif** >1 puan kötü.
- Sol-sağ designer symmetry source **%100 → target %100** olarak korunuyor.
- Üst-alt source symmetry yaklaşık **%97,70**. İlk motor sonucu yaklaşık **%86,31** idi;
  measured-source-phase kullanan candidate-backed soft symmetry sonrası **%89,64**'e çıktı.
  Nearest aynı ölçümde yaklaşık **%86,75**.
- Edge density: source **%36,24**, RugScale **%40,50**, Nearest **%38,35**.
- Palette-area TV drift: RugScale yaklaşık **%3,04**, Nearest **%0,42**.
- GitHub runner'da son RugScale resize yaklaşık **3,34 sn** tamamlandı.

### Soft symmetry kuralı

`~%97` source symmetry hard mirror sayılmaz; intentional asymmetric ornament körlemesine
çoğaltılmaz. Bunun yerine:

1. source'taki en iyi ±3 px mirror phase ölçülür,
2. soft axis target'a recenter edilmez; measured source phase target yoğunluğuna ölçeklenir,
3. yalnız source'ta kendi mirror partneriyle gerçekten aynı palette indexe sahip source pair'leri
   candidate-backed reconciliation'a girebilir,
4. her iki target footprint aynı palette indexi yeterli confidence ile desteklemiyorsa değişiklik
   yapılmaz,
5. exact/hard symmetry axis'i varsa soft pass önce çalışır, hard pass en son çalışarak exact axis'i
   tekrar authoritative hale getirir.

Bu ayrım B163A'da kritik oldu: sol-sağ exact symmetry korunurken, top-bottom near-symmetry
iyileştirildi; ilk soft-pass denemesinde hard left-right pass'ten sonra top-bottom yazıldığı için
sol-sağ symmetry %96,2'ye düşmüştü. Pass ordering düzeltilince tekrar %100 oldu.

### Audit skorunu yorumlama

Strict motif audit özellikle 1×3, 1×4 gibi çok küçük `Branch` primitive'lerinde exact target-cell
fazına ağır ceza verir. Bu nedenle strict Branch ortalaması yaklaşık %70 iken 1-cell-neighbourhood
projection-retention aynı gerçek rasterda yaklaşık **%98,3**'tür. Motor geliştirmesinde yalnız strict
skoru yükseltmek için çizgiyi zorla aynı target hücresine taşımak doğru değildir; branch continuity,
endpoint/junction, palette safety, repeat ve designer symmetry birlikte değerlendirilmelidir.


## B163A drawing-style self-training — 23 Eylül 2026

Çizim/redraw tarafı için ayrı self-supervised eğitim akışı eklendi. Buradaki "eğitim" bir neural
network training'i değildir; original source motif rasterı ground-truth teacher olarak kullanılır.
Seçilen her gerçek motif immutable original source'dan beş ayrı redraw style ile yeniden çizilir:
`Balanced`, `PreserveBranches`, `ContourFirst`, `ConnectivityFirst`,
`DetailAndConnectivity`. Çıktılar source'tan hedefe projekte edilen exact motif mask/color
ground-truth ile F1 + IoU + indexed-color agreement + boundary retention üzerinden skorlanır.

B163A eğitim run'ı:

- **242** gerçek motif sample,
- **122** learned motif family,
- training süresi yaklaşık **0,175 sn** (audit/detector süresi hariç),
- Branch: 60 sample, mean best **%91,88**; 56/60 `Balanced`,
- LeafLike: 60 sample, mean best **%88,64**; 57/60 `Balanced`,
- Compound: 60 sample, mean best **%91,74**; 30/60 `PreserveBranches`, 25/60
  `DetailAndConnectivity`,
- Primitive: 60 sample, mean best **%95,80**; 41/60 `DetailAndConnectivity`,
- LargePart: 2 sample, mean best **%98,21**; 2/2 `DetailAndConnectivity`.

Overall mean style score: `Balanced %90,27`, `PreserveBranches %91,27`,
`ContourFirst %90,42`, `ConnectivityFirst %91,27`, `DetailAndConnectivity %92,02`.
`ContourFirst` bu B163A training setinde hiçbir sample'ın winner'ı olmadı.

Bu sonuç yalnız raporda bırakılmadı. Unknown motif family için cold-start redraw order gerçek
training ölçümüne göre güncellendi; ayrıca Core `MotifRepairEngine.PreferredColdStartStyle`
doğrudan API kullanan kodlar için aynı prior'ı uygular. Family/user feedback mevcutsa
`MotifMemory` confidence yine bu prior'ın önüne geçer. Böylece sistem bir tek B163A'ya hard-code
edilmiş olmadan, bilinmeyen motifte daha iyi ilk çizimi seçer ve kullanıcı feedback'i geldikçe
family-specific öğrenmeye devam eder.

GitHub Actions `RugScale B163A drawing self-training` her training run'ında
`trained-memory.json`, `drawing-training.csv` ve `drawing-training.md` artifact üretir.


## N69 Curve-tool kalite eğitimi ve büyütme motoru — 23 Eylül 2026

Dört gerçek indexed N69 tasarımı ayrı curve fixture seti olarak RugScale CI'a alındı:

- `C071C_BEIGE_N69`: 640×1380, kalite 40×60,
- `B996A_BEIGE_N69`: 640×1150, kalite 40×50,
- `C004A_BEIGE_N69`: 640×1150, kalite 40×50,
- `C069A_CREAM_N69`: 640×1380, kalite 40×60.

Bu desenlerde temel kural artık **fiziksel ebat ile piksel kalınlığını birbirinden ayırmak**.
RugScale büyütmede motif/curve geometrisi hedef ebatla büyür; Curve-tool kalem kalınlığı ise yalnız
`targetQuality/sourceQuality` oranıyla değişir. Aynı kalitede 160→256 gibi fiziksel büyütme bir
3 px yayı 5 px blok haline getirmez. Kalite değişirse aynı source curve yeni warp/weft ızgarasına
yeniden rasterize edilir.

`CurveScaleEngine` şu kaynak kanıtlarını kullanır:

- 8-connected indexed component ayrıştırma,
- Zhang-Suen skeleton/centre-line çıkarımı,
- padded chamfer distance transform ile **skeleton boyunca local source pen width** ölçümü,
- source curve path'in target koordinatlarına yeniden çizimi,
- local pen width'in yalnız warp/weft kalite oranıyla ölçeklenmesi,
- palette-index safety,
- exact/phase-shifted designer symmetry koruması,
- shrink tarafında additive source-backed curve restoration.

Local pen width önemlidir: tek bir component-wide ortalama yerine yayın bir bölümündeki 1 px,
başka bölümündeki 3 px veya taper/widen davranışı source rasterdan ayrı ayrı ölçülür. Böylece motor
curve'ü yalnız geometrik olarak büyütmez; original indexed çizim karakterini de taşır.

Shrink refinement transactionaldır. Generic RugScale repeat/topology/symmetry sonucu önce baseline
olarak saklanır. Curve restoration güçlü bir source symmetry eksenini geriye götürürse işlem geri
alınır. B163A regresyonunda bu koruma exact LR ve strong near-symmetry davranışını eski doğrulanmış
seviyede tutar.

Ayrı GitHub Actions workflow'u `RugScale four-design curve suite` dört fixture üzerinde:

- 80% same-quality shrink,
- original ebada round-trip,
- Nearest baseline,
- 160% **same-quality direct enlargement**,
- curve/thin component ve pen kalınlığı,
- ±1px geometry agreement,
- edge density,
- palette-area drift,
- exact symmetry,
- palette safety

ölçer.

Son doğrulanmış 80% → original round-trip sonuçları:

| Tasarım | RugScale exact | Nearest exact | RugScale ±1px | Nearest ±1px |
|---|---:|---:|---:|---:|
| C071C | 91.46% | 89.45% | 97.38% | 97.39% |
| B996A | 92.34% | 91.40% | 97.93% | 97.95% |
| C004A | 92.04% | 91.18% | 98.08% | 97.78% |
| C069A | 89.49% | 90.57% | 97.26% | 97.37% |

C069A exact-cell metriğinde Nearest yaklaşık 1 puan önde olsa da RugScale palette-area drift'i
%1.77 ile Nearest'in %2.82 değerinden daha iyi ve ±1px geometry farkı yalnız ~0.11 puandır. Bu nedenle
motoru yalnız exact-cell metriğine overfit edip curve geometrisini bozacak bir zorlamaya gidilmedi.

160% same-quality direct enlargement pen kalınlığı ölçümlerinde RugScale block-scaling yapmıyor;
Nearest'in fiziksel ebatla şişirdiği stroke yerine source-guided local pen-width redraw uygulanıyor.
Moderate same-quality enlargement (<~1.45 physical scale) şimdilik kalibre edilmiş conservative
centre-sampling yolunda kalır; gerçek dört-desende 125% round-trip'te source-guided redraw henüz
baseline'ı geçmediği için motor bilerek agresif davranmaz. Kalite değişimi varsa bu shortcut
kullanılmaz ve curve yeni quality grid'ine yeniden rasterize edilir.

Core regresyon + four-design curve suite + B163A real-design doğrulaması bu davranış için CI gate'tir.


## Mimari düzeltme: Motif RugScale ve Curve & Fill ayrıldı — 23 Eylül 2026

Gerçek `B996A_BEIGE_N69` source ile 800×1800 RugScale çıktısı yan yana görsel incelendi.
Önceki yaklaşım mimari olarak yanlıştı: curve/skeleton katmanı normal `RugScale` içine
bağlanmıştı. Bu, motif tabanlı desenlerle curve-tool ağırlıklı, **dış kontur + dolgu bölgesi**
mantığında çizilmiş desenleri aynı algoritmaya zorladı. Gerçek çıktıda bunun sonucu:

- oval/yay konturlar source kadar düzgün değildi,
- bazı kavisler skeleton/stroke gibi yeniden yorumlandı,
- dolu yaprak/ornament parçalarında iç renk alanları daraldı veya öldü,
- kullanıcının özellikle ayrı mod istemesine rağmen iki problem sınıfı tek seçenek altında kaldı.

Bu davranış kaldırıldı.

Resize UI artık iki ayrı uzman mod gösterir:

- **RugScale — motif & topology designs**: discrete motif, branch, rapport/repeat ve motif topology
  için mevcut motif motoru. Curve/fill reconstruction bu moda artık ASLA otomatik girmez.
- **RugScale Curve & Fill — outlined / curved filled designs**: B996A/C071C/C004A/C069A benzeri
  Curve-tool karakterli; düzgün dış çizgilerle çevrilmiş ve içi indexed renklerle dolu floral/
  ornamental desenler için kullanıcı tarafından açıkça seçilen ayrı mod.

### Curve & Fill yeniden çizim modeli

Yeni `CurveFillScaleEngine` filled ornament'i skeleton'a indirmez. Her **kullanılmış palette index**
bağımsız categorical dolu bölge kabul edilir.

1. Source'taki her indexed renk bölgesi için dış/iç sınırdan signed-distance field çıkarılır.
2. Mesafe hesabı source warp/weft kalitesinden fiziksel pixel aspect'i kullanır; 40×50 grid kare
   kabul edilmez.
3. Signed fields hedef raster üzerinde bilinear olarak örneklenir. Bu RGB blending değildir;
   interpolasyon yalnız **sınır geometrisi** üzerindedir.
4. Her target pikselde en güçlü categorical bölge kazanır; yazılan değer daima source'ta gerçekten
   kullanılan palette index'tir.
5. Böylece outer curve/oval boundary tekrar rasterize edilirken bölgenin İÇİ original indexed
   rengiyle dolu kalır. Ayrı stroke pass'in dolgunun üstünden geçip rengi öldürmesi yoktur.
6. Exact source symmetry varsa sonuçta korunur.

Python/C# prototip karşılaştırmasında bu contour+fill yaklaşımı gerçek B996 640×1150 → 800×1800
örneğinde önceki RugScale çıktısındaki kırık/jagged curve ve hollowed-fill davranışını kaldırıp source
görünümüne belirgin biçimde daha yakın categorical bölgeler üretmiştir. GitHub curve suite artık
eski motif `ScaleMode.RugScale` yerine `ScaleMode.CurveFill` çalıştırır ve B996 için ayrıca
`B996A_BEIGE_N69_curve_fill_800x1800.bmp` artifact üretir.

Eski `CurveScaleEngine` deneyleri tarihsel geliştirme kaydı olarak kodda/audit geçmişinde bulunabilir;
ancak normal motif RugScale execution path'inden çıkarılmıştır. Ürün kontratı bundan sonra:
**motif motoru ve curve/fill motoru ayrı kullanıcı seçimleridir.**


## Curve-tool inverse model: family + roundness recovery — 24 Eylül 2026

`RugScale Curve & Fill` içindeki trusted 1x1 / Pixel-Cord çizgiler artık yalnız edge-for-edge
ölçeklenmiyor. `ToolFaithfulCurveStyleLearner`, immutable source rasterı teacher kabul edip
RugScale host application'in **kendi** `CurveRasterizer` family'lerine tersine fit yapar:

- `SplineThroughPoints`,
- `Bezier`,
- `Spline`,
- ve complexity-regularized polyline/graph fallback.

Through-Points için source kontrol noktaları curve üstünde olduğu için ordered raster chain üzerinde
kontrol-index optimizasyonu yapılır ve roundness grid'i birlikte aranır. Bezier'de handle noktaları
raster üstünde olmadığı için önce chord-length parameterization + least-squares effective cubic
handle fit'i çıkarılır, ardından handle noktaları RugScale host application rasterizer skoruna göre lokal optimize
edilir. Bezier/Spline'da historical slider roundness ile handle displacement rasterdan tekil olarak
ayırt edilemeyebildiği için motor "eski slider değerini tahmin etmek" yerine **aynı target
geometrisini üreten effective model** öğrenir.

Güvenli fallback kuralı değişmedi:

- çizgi düz/köşeli ise curve fit zorlanmaz,
- kapalı 1x1 outline loop open-curve family'ye zorlanmaz,
- learned compact model complexity-regularized graph baseline'ı geçmezse exact source graph replay
  kullanılır,
- same-quality 1x1 Pixel Cord pen yine 1x1 tool karakterinde kalır.

### Self-training sonucu

GitHub curve suite artık 60 deterministic teacher örneği üretir. Source curve yalnız raster olarak
learner'a geri verilir; ardından learned model ve edge-for-edge graph **160% target ölçekte** gerçek
teacher curve ile karşılaştırılır. Bu, family adını tahmin etmekten daha önemli olan gerçek resize
hedefini ölçer.

| Teacher family | Accepted | Family-correct | Learned target exact F1 | Graph exact F1 | Kazanç |
|---|---:|---:|---:|---:|---:|
| SplineThroughPoints | 27/30 | 27/27 | **83.52%** | 63.25% | **+20.27 puan** |
| Bezier | 16/18 | 11/16 | **77.73%** | 66.26% | **+11.47 puan** |
| Spline | 9/12 | 0/9 | **68.31%** | 63.46% | **+4.85 puan** |

Through-Points roundness MAE **0.046**. Spline historical family etiketi sentetik örneklerde
ayırt edilebilir değil; buna rağmen learned equivalent compact curve target rasterda graph
ölçeklemeden daha doğru. Bu nedenle training objective **historical UI label** değil, target-scale
indexed-raster fidelity'dir.

CI artık aşağıdaki inverse-model guardrail'lerini de enforce eder:

- Through-Points accepted coverage >= 20/30,
- accepted Through-Points family precision >= 95%,
- Through-Points roundness MAE <= 0.12,
- her Curve family için learned target exact-F1, graph baseline'dan en az +0.02 daha iyi.

### Dört gerçek N69 üzerinde source-style inference

160% same-quality direct run'da gerçek source çizgilerinden güvenle öğrenilen chain sayıları:

- C071C: **292** learned curve — 241 Through Points / 2 Spline / 49 Bezier,
- B996A: **878** — 516 / 36 / 326,
- C004A: **367** — 203 / 4 / 160,
- C069A: **201** — 122 / 2 / 77.

Geri kalan binlerce kısa, köşeli, kapalı veya düşük-güvenli chain bilinçli olarak graph fallback'te
kalır; model her şeyi curve'e çevirmeye çalışmaz.

Aynı translated pixel-chain motifleri için translation-invariant style-fit cache eklendi. Son gerçek
audit'te tekrar kullanılan fit sayıları C071C **8,295**, B996A **5,911**, C004A **3,099**,
C069A **10,160**. Cache traversal yönünü canonicalize ETMEZ; ters yönde izlenen chain yeniden fit
edilir. Böylece optimizasyon çizim sonucunu değiştirmeden yalnız aynı yönlü translated tekrarları
yeniden kullanır.

Son regression değerleri korunuyor: dört design palette-safe; C004A exact TB symmetry exact kalıyor;
80%→original roundtrip exact agreement sırasıyla **94.76% / 94.64% / 95.94% / 94.62%** ve ±1px
geometry **99.93% / 99.87% / 99.95% / 99.90%**.


## Leaf / Petal Arcs: paired designer-curve reconstruction — 24 Eylül 2026

Curve & Fill taşma/ownership problemini çözdükten sonra gerçek B996 görselinde kalan ana kalite farkı
"piksel olarak yakın ama tasarımcı yayı kadar estetik olmayan" uzun leaf/petal sınırlarıydı. Bu
problem genel `CurveFill` içine zorlanmadı; ayrı **`ScaleMode.LeafPetalArcs`** uzman modu olarak
uygulandı. Resize UI metni: **RugScale Leaf / Petal Arcs — elegant tapered floral curves**.

Yeni modun kontratı:

1. Güvenli başlangıç her zaman normal `CurveFillScaleEngine` sonucudur.
2. Aynı renk birden fazla yaprak/gövdeyi tek connected component içinde taşıyorsa
   `LeafPetalLobeExtractor` source skeleton branch'lerinden ayrı terminal lobe'lar çıkarır.
3. Uzamış dolu lobe `LeafPetalArcClassifier` + `LeafPetalMedialAxisBuilder` ile base→apex
   centerline/width-profile modeline çevrilir.
4. Source'ta güvenilir ayrı outline palette rolü varsa centerline tahmini yerine
   **iki gerçek dış designer eğrisi** tercih edilir. `LeafPetalBoundaryCurveBuilder`, region'a
   gerçekten temas eden source outline piksellerinden sol/sağ tarafı çıkarır.
5. İç beyaz slit/dekoratif çizgi dış kenarı ele geçiremez. Source outline graph traversal artık
   directional weighted path search kullanır: base→apex ilerleme, doğru normal tarafı ve lokal
   çizgi devamlılığı puanlanır; source outline dışında shortcut üretilemez.
6. Her dış kenar `LeafPetalBoundaryCurveFitter` ile RugScale host application
   `SplineThroughPoints` family'sine tersine fit edilir. Source endpoint tangent'i yanında 11
   noktadan **macro tangent profile** da eşleşmek zorundadır. 6–7 kontrol noktası staircase'i
   takip etmesin diye artan complexity penalty alır.
7. Hedef ebatta iki kenar bağımsız bırakılmaz.
   `LeafPetalBoundaryPairOptimizer` source control noktalarını değiştirmeden iki tarafın
   roundness değerlerini birlikte arar. Hedef fonksiyon:
   source corridor + macro tangent flow + turn/curvature profile + paired width profile +
   width smoothness. Tüm standart RugScale host application roundness grid'i aranır; ilk fit'in çevresine kilitlenmez.
8. Rasterizer yalnız source outer-boundary koridorunda değişiklik yapar. İç slit ile aynı palette
   index'i taşıyan beyaz bir pikselin silinmesi için ayrıca gerçek recovered outer path'e yaklaşık
   1.2 source-pixel mesafede olması gerekir. Böylece dış yayı temizlerken iç çizgi ölmez.
9. Güvenli paired fit kurulamazsa mod tahmin üretmez; normal Curve & Fill baseline korunur.

Yeni regresyonlar paired designer curve'ün target-scale geometrisini, inner slit retention'ı ve
joint roundness optimizer'ın uyumsuz iki kenarı hedef ölçekte kötüleştirmemesini kilitler.

### B996 kalibrasyon diagnostikleri

Curve-suite B996 640×1150 → 800×1320 kullanıcı senaryosu için ayrıca:

- full Leaf/Petal artifact,
- kullanıcının işaretlediği yeşil yaprak focus crop'ları,
- raw lobe CSV,
- candidate-stage CSV,
- paired source-path piksel sayıları,
- source left/right roundness,
- 800×1320 joint-optimized left/right roundness,
- pair optimizer'ın ayar değiştirip değiştirmediği

bilgilerini üretir. Böylece whole-design average iyi görünürken lokal kötü bir yay saklanamaz.

> CI notu (24 Eylül 2026): son GitHub Actions denemeleri kod adımlarına ulaşmadan
> `runner_id=0 / steps=[]` ile kapanıyor. Aynı run'ın manual failed-job rerun attempt'i de runner
> almadan düştü. Bu nedenle bu en yeni Leaf/Petal commit'leri için kırmızı Action durumu bir
> compile/test sonucu olarak yorumlanmamalıdır; runner yeniden çalışana kadar son değişiklikler
> **CI doğrulanmış kabul edilmez**. Önceki Curve & Fill/B163A green baseline korunarak geliştirme
> aynı feature branch'inde sürdürülmektedir.
