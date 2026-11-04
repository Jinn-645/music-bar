# MusicBar

Thanh taskbar nằm im ba mươi năm rồi. Cho nó hát đi.

**Tải về:** [bản cài đặt](https://github.com/lvt1693/music-bar/releases/latest/download/MusicBar-1.3.0-Setup.exe)
hoặc [bản portable](https://github.com/lvt1693/music-bar/releases/latest/download/MusicBar-1.3.0-portable.zip)
(Windows 10 2004 trở lên hoặc Windows 11, 64-bit, không cần cài .NET).

- [x] **04/11** Bản 1.0 lên GitHub: 14 bộ áo cho thanh tiến trình, karaoke đủ dấu, taskbar
  Windows 11 trong suốt, trung tâm điều khiển riêng, installer, bản portable và một video
  giới thiệu có taskbar biết ngáp.
- [x] **04/11** Bản 1.1: bảy kiểu nút ⏮ ⏯ ⏭ (có cả đĩa than xoay và phím cassette), giao
  diện tiếng Anh mặc định với tiếng Việt chỉ cách một cú bấm, bản cài nhẹ RAM hơn hẳn
  (143 MB xuống còn khoảng 58 MB), và vài chỗ chỉnh cho Windows 10.
- [x] **04/11** Bản 1.1.1: hết cảnh widget nhảy vào góc đè lên khay mỗi khi mở Quick Settings
  của Windows (lúc Control Center của MusicBar đang tắt).
- [x] **04/11** Bản 1.2: taskbar dọc (lời bài hát xếp mỗi chữ một dòng), nhiều màn hình (hết
  mờ nhòe ở màn phụ, chọn màn riêng cho thanh tiến trình và cho trình phát), 4 hướng chạy của
  thanh tiến trình, chế độ tối giản, danh sách Wi-Fi và Bluetooth ngay trong Control Center, và
  trang cài đặt sắp xếp lại cho dễ tìm.
- [x] **04/11** Bản 1.3: cái mặt trong video giới thiệu dọn lên ở hẳn trên taskbar (8 kiểu mặt,
  nhún theo nhạc, ngủ gật về đêm, bấm, xoa, cuộn chuột là nó phản ứng), nền taskbar sáng cho
  Windows chế độ sáng, Control Center không còn tràn khỏi màn hình khi nhiều thông báo.

![Thanh taskbar ngủ gật, tiếng cào đĩa, icon MusicBar rơi xuống và đèn sân khấu bật sáng](docs/wake-up.gif)

Nhạc phát cả ngày mà taskbar chỉ biết hiện mỗi cái đồng hồ. MusicBar biến cả chiều ngang
taskbar thành một thanh tiến trình đổi màu theo ảnh bìa và nhún nhảy theo nhạc thật, đặt ảnh
bìa với nút ⏮ ⏯ ⏭ ngay cạnh khay hệ thống, và cho lời bài hát chạy karaoke trên khoảng trống
của taskbar. Taskbar vẫn bấm, vẫn kéo, vẫn ghim app như cũ. MusicBar chỉ ở dưới chân nó.

Nó bắt mọi app đang phát nhạc qua Windows Media Session: Spotify, YouTube trong trình duyệt,
Media Player, Apple Music… Không có nhạc thì nó lặn mất, taskbar về nguyên trạng.

## Nó làm gì

- **Thanh tiến trình trải hết taskbar**, nằm dưới các icon. Mười bốn kiểu (bảng bên dưới),
  kiểu nào cũng lấy màu từ ảnh bìa, màu app đang phát, màu nhấn của Windows hoặc màu bạn chọn.
  Chạy theo 4 hướng: trái sang phải, phải sang trái, từ giữa ra hai đầu, hoặc từ hai đầu vào
  giữa.
- **Taskbar nằm đâu cũng chạy:** dưới, trên, hay dựng dọc bên trái / bên phải. Taskbar dọc thì
  thanh tiến trình chạy từ dưới lên, ảnh bìa và nút ⏮ ⏯ ⏭ xếp chồng, lời bài hát hiện mỗi chữ
  một dòng, còn thẻ bài hát và Control Center mở sang bên cạnh.
- **Nhiều màn hình:** chọn thanh tiến trình hiện ở màn chính, tất cả hay một màn, và chọn riêng
  màn cho trình phát với lời bài hát (Cài đặt → Chung → Màn hình).
- **Tối giản nếu thích:** ẩn trình phát mà giữ lời (lời tự dời vào khoảng trống lớn nhất), hoặc
  ẩn cả hai, chỉ để thanh tiến trình chạy (Cài đặt → Trình phát).
- **Một cái mặt sống trên taskbar** (chi tiết ở dưới): nhún theo nhạc, ngủ gật về đêm, cười khi
  bị chọc, giận khi bị chọc hoài.
- **Nền sáng hay tối:** mặc định theo chế độ sáng / tối của Windows. Nền sáng thì lời bài hát,
  nút và cái mặt chuyển sang màu tối cho icon đen của Windows vẫn rõ.
- **Nhảy theo nhạc thật.** Các kiểu động nghe chính âm thanh đang ra loa (WASAPI loopback),
  xử lý ngay trong máy, không gửi đi đâu. Tắt đi thì chúng nhún theo nhịp giả lập.
- **Lời bài hát đồng bộ** từ [LRCLIB](https://lrclib.net), tô màu karaoke từng chữ, 8 hiệu ứng
  đổi dòng (cuộn, mờ, quét, phóng, nảy từng chữ, gõ chữ, băng chạy…). Lời tự tìm khoảng
  trống lớn nhất trên taskbar, hoặc trượt ra cạnh ảnh bìa. Lời đã tải lưu thành `.lrc`, lần
  sau khỏi cần mạng. Lệch thì chỉnh sớm / trễ 0,25 giây cho từng bài.
- **Kiểu chữ tự chọn:** cỡ, màu, độ đậm, viền (nhỏ là viền sắc, lớn là quầng mềm), và 10
  phông miễn phí đủ dấu tiếng Việt: Be Vietnam Pro, Lexend, Montserrat, Quicksand, Nunito,
  Comfortaa, Playfair Display, Lora, Dancing Script, Pacifico.
- **Vùng yên tĩnh quanh lời:** ngay dưới dòng lời đang hiện, thanh tiến trình dịu xuống còn
  khoảng 30 %, dài ngắn theo từng dòng, nên chữ luôn đọc rõ.
- **Taskbar Windows 11 trong suốt**, tự làm, không cần cài thêm gì (chi tiết ở dưới).
- **Trung tâm điều khiển riêng:** bấm nút loa trên khay là ra một bảng gom thông báo, cài
  đặt nhanh, âm lượng và thiết bị phát. Danh sách loa, mạng Wi-Fi, thiết bị Bluetooth trượt ra
  bên cạnh, chọn là đổi ngay.
- **Chỉnh taskbar của Windows:** cỡ nhỏ / vừa (đọc đúng cài đặt *Kích thước taskbar* mới của
  Windows 11), Start và icon ở giữa. Chạy được cả với taskbar kiểu Windows 10 của
  [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher).
- **Thẻ thông tin bài hát:** giữ chuột trên ảnh bìa là ra ảnh lớn, ca sĩ, album, năm, thể loại.
- **Tiếng Anh và tiếng Việt:** giao diện mặc định là tiếng Anh, đổi sang tiếng Việt ở ô ngôn
  ngữ góc trên bên trái cửa sổ cài đặt. Đổi là đổi ngay, không cần mở lại app.
- **Bảy kiểu nút ⏮ ⏯ ⏭** (Cài đặt → Trình phát → Kiểu nút điều khiển, hoặc chuột phải lên
  widget): Đĩa sáng, Tối giản, Viên kẹo, Vòng tiến trình (vòng quanh nút phát chạy theo bài),
  Đĩa than (đĩa xoay khi phát, nhãn đĩa là ảnh bìa), Phím cassette (phím bấm lún, có đèn báo)
  và Bong bóng (bấm là nảy như thạch).

## 14 bộ áo

Chọn ở **Cài đặt → Thanh tiến trình** (bản xem trước chạy thật với bài đang phát) hoặc chuột
phải → Kiểu thanh tiến trình.

![Thanh taskbar mặc bộ Cực quang trên sân khấu](docs/outfits.jpg)

| Kiểu | Trông ra sao |
| --- | --- |
| Cực quang (mặc định) | Ba dải sáng uốn lượn theo bảng màu bài hát, biên độ theo nhạc |
| Lưới màu | Những mảng màu lớn từ ảnh bìa trôi chậm, hòa vào nhau, phồng lên theo từng dải tần |
| Sợi tơ | 14 sợi tơ mảnh xoắn quanh nhau, se lại thành một sợi ở đầu phát |
| Ánh kim | Giấy kim tuyến đổi sắc quanh màu bài hát, vệt sáng lướt qua mặt giấy |
| Ánh đèn mờ | Đốm đèn bokeh xa gần, mỗi đốm nhấp nháy theo một dải tần |
| Mực loang | Màu nước loang sau đầu phát, giọt mực vẫn nở tiếp sau khi đầu phát đi qua |
| Dải màu ảnh bìa | Màu từng cột của ảnh bìa trải thành dải, lộ dần theo bài |
| Điện ảnh | Ảnh bìa nhòe sâu, trôi chậm như một cảnh phim |
| Ống neon | Ống neon dọc mép, chập chờn, hắt sáng xuống taskbar |
| Chất lỏng | Mặt chất lỏng gợn sóng ở đầu phát, bọt khí li ti |
| Phổ âm | Cột phổ âm thanh thật, phần đã phát được tô màu |
| Dạng sóng | Sóng âm kiểu SoundCloud, vẽ dần dạng sóng thật khi phát |
| Ô LED | Ma trận ô vuông lấp lánh, cột đầu phát kiêm đồng hồ mức âm |
| Cổ điển | Phủ màu và quầng sáng ở đầu phát |

Kiểu động vẽ 30 khung / giây khi đang phát (các kiểu trôi chậm 20 khung), đứng yên khi tạm dừng.

## Cài

- **Bản cài đặt** (`MusicBar-1.2.0-Setup.exe`): cài cho riêng tài khoản của bạn vào
  `%LOCALAPPDATA%\Programs\MusicBar`, không hỏi quyền admin, có mục trong Start và gỡ được
  trong Cài đặt → Ứng dụng. Cài đè hay gỡ đi thì MusicBar được báo thoát trước, để trả taskbar
  và khay về đúng như Windows sắp xếp.
- **Bản portable** (`MusicBar-1.2.0-portable.zip`): giải nén ra đâu cũng được, chạy
  `MusicBar.exe`.

Lên từ bản 1.1 trở về trước thì khởi động lại Explorer (hoặc đăng xuất rồi vào lại) một lần,
để module làm trong suốt taskbar bản mới thay bản cũ. Chưa làm thì MusicBar vẫn chạy, chỉ là
có nhiều màn hình thì nó tạm phủ mờ thay vì làm trong suốt, và cài đặt sẽ nhắc.

Hai bản là cùng một app, đã gói sẵn .NET 10 nên không cần cài gì thêm, và chạy tốn khoảng
55–60 MB RAM (cột Memory của Task Manager, đo trên máy thử). Lần đầu chạy, cửa sổ
cài đặt tự mở; sau đó nhấp biểu tượng ở khay để mở lại. Muốn nó chạy cùng Windows thì bật
**Khởi động cùng Windows** trong cài đặt. Cài đặt, lời bài hát và nhật ký nằm ở `%LOCALAPPDATA%\MusicBar`
cho cả hai bản.

App chưa có chữ ký số, nên lần đầu SmartScreen sẽ cản: bấm **More info → Run anyway**.

Dòng lệnh, nếu bạn thích:

```powershell
MusicBar.exe --demo       # bài hát mẫu có sẵn lời, không cần trình phát nào
MusicBar.exe --settings   # mở cài đặt (cả khi app đang chạy)
MusicBar.exe --exit       # thoát, trả taskbar về như cũ
```

## Taskbar trong suốt

Windows không cho vẽ "vào trong" taskbar, nên thanh tiến trình là một cửa sổ riêng giữ ngay
dưới taskbar, và chỉ nhìn xuyên qua được khi taskbar trong suốt. Bật ở **Cài đặt → Taskbar →
Làm taskbar trong suốt**. Đang chạy [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB)
thì MusicBar để nó lo.

- **Taskbar kiểu Windows 10** (Windows 10, ExplorerPatcher): đặt thuộc tính DWM của cửa sổ
  taskbar, và đặt lại mỗi khi Explorer tự đổi nền (mở Start, đổi theme).
- **Taskbar Windows 11 gốc:** nền là một phần tử XAML bên trong Explorer. MusicBar nạp một
  module nhỏ (`MusicBar.TaskbarTap.dll`, nhúng sẵn trong app) vào Explorer theo cách
  TranslucentTB làm, viết lại độc lập. Module dùng API chẩn đoán XAML công khai để thay brush
  nền bằng brush trong suốt, giữ brush gốc để trả lại. Nó chỉ giữ nền trong suốt khi MusicBar
  còn sống: MusicBar thoát hay bị tắt ngang là taskbar tự có lại nền.

| Chế độ lớp | Khi nào |
| --- | --- |
| Tự động (mặc định) | Taskbar trong suốt được thì nằm dưới icon, không thì phủ mờ lên trên |
| Nằm dưới icon | Taskbar đã trong suốt: trông như chính nền taskbar |
| Phủ mờ lên taskbar | Taskbar không trong suốt: phủ lên trên, tự giới hạn độ đậm để icon vẫn rõ |

## Trung tâm điều khiển

![Bảng điều khiển của MusicBar: thông báo, cài đặt nhanh, âm lượng, danh sách loa trượt ra bên trái](docs/control-center.jpg)

Khi MusicBar chạy, nút mạng / âm lượng (Windows 11) hoặc nút Control Center (ExplorerPatcher)
mở bảng của MusicBar, từ trên xuống:

- **Thông báo** của Windows: bấm để mở app gửi, × để bỏ từng cái, *Xóa tất cả* để dọn.
- **Cài đặt nhanh:** Wi-Fi, Bluetooth, Ánh sáng đêm, Phụ đề trực tiếp bật / tắt thật. *Màn
  hình chiếu* (như Win+P) và *Truyền màn hình* (Miracast) mở danh sách trượt ra bên cạnh.
- **Wi-Fi:** bấm mũi tên bên ô Wi-Fi là ra danh sách mạng. Bấm một mạng để kết nối (nhập mật
  khẩu, có nút hiện mật khẩu, tick *Tự động kết nối*), chuột phải để kết nối / ngắt / quên
  mạng. Từ Windows 11 24H2, Windows chỉ cho app xem danh sách Wi-Fi khi được dùng vị trí; máy
  tắt vị trí thì MusicBar mở thẳng danh sách Wi-Fi của Windows.
- **Bluetooth:** thiết bị đã ghép đôi và thiết bị ở gần. Ghép đôi (mã PIN hiện ngay trong bảng),
  kết nối / ngắt tai nghe và loa, gỡ thiết bị, cũng có menu chuột phải.
- **Âm lượng** và **thiết bị phát**: danh sách loa trượt ra, chọn là đổi ngay.

Bảng gốc của Windows vẫn ở đó: Win+A và Win+N. Tắt tính năng hoặc thoát MusicBar là khay về
nguyên như Windows sắp xếp.

## Cái mặt

Anh em xem video giới thiệu thích cái mặt quá, nên giờ nó ở hẳn trên taskbar, cạnh trình phát
(đổi chỗ ở **Cài đặt → Cái mặt**).

- **8 kiểu mặt:** Cổ điển (đúng cái mặt trong video), Cổ điển nữ, Anime nữ, Anime nam, Người
  ngoài hành tinh, Rô-bốt (mắt LED, miệng là cột sóng nhạc), Mèo, Ngáo.
- **Sống theo tình hình:** có nhạc thì nhún theo nhịp và hát theo, qua bài mới thì mắt sao, tạm
  dừng thì ngó quanh chờ, nhạc tắt sau một buổi dài thì rưng rưng.
- **Sống theo giờ:** sáng ngáp rồi vươn vai, gần trưa thèm ăn, ăn xong gật gù, đúng giờ chẵn giật
  mình, khuya thì mí mắt sụp, bạn rời máy là nó ngủ (có Zzz), quay lại là nó dậy.
- **Không đứng yên:** chớp mắt, liếc ngang liếc dọc, ngân nga, nháy mắt, lè lưỡi, mắt nhìn theo
  con trỏ khi lại gần.
- **Trêu được:** bấm là cười, bấm liên tục thì bực rồi nổi cáu rồi dỗi quay đi; giữ chuột rê qua
  lại (hoặc rê con trỏ qua lại trên nó) là cười chảy nước mắt rồi thả tim; bấm đúp mắt sao; cuộn
  chuột là chóng mặt; đang ngủ mà chọc là giật mình.
- Chuột phải lên nó để đổi kiểu hay ẩn đi. Không có nhạc mà MusicBar ẩn thì mặt vẫn ở lại ngủ gật
  (tắt được).

## Lời bài hát

![Lời bài hát chạy karaoke trên taskbar bằng phông Playfair Display](docs/karaoke.jpg)

Tìm theo thứ tự: file `.lrc` trong thư mục lời (`%LOCALAPPDATA%\MusicBar\Lyrics`, tên
`Ca sĩ - Tên bài.lrc`, tự thả file của mình vào cũng được), rồi tới LRCLIB theo tên bài, ca sĩ,
album và thời lượng. Tiêu đề kiểu YouTube được dọn bớt ("Official MV", "(Lyrics)", "feat.")
trước khi tìm. Bài không có lời được nhớ 3 ngày để khỏi hỏi lại. Mạng cần proxy thì điền ở
**Cài đặt → Lời bài hát → Proxy**.

## Build

Cần .NET 10 SDK, và MSVC (Visual Studio hoặc Build Tools, workload "Desktop development with
C++") cho module taskbar Windows 11.

```powershell
dotnet build src/MusicBar/MusicBar.csproj
dotnet run --project src/MusicBar -- --demo
.\installer\build.ps1 -Version 1.3.0     # ra artifacts\: Setup.exe (Inno Setup 6) và portable.zip
```

## Bên trong

```
src/MusicBar/
  App.xaml(.cs)                 khởi động, một phiên bản duy nhất, khay hệ thống
  Models/AppSettings.cs         mọi tùy chọn (%LOCALAPPDATA%\MusicBar\settings.json)
  Services/MediaService.cs      theo dõi session, nội suy vị trí phát, điều khiển
  Services/LyricsService.cs     cache .lrc + LRCLIB;  LrcParser.cs đọc LRC
  Services/AudioOutputs.cs      thiết bị phát, âm lượng, đổi loa mặc định
  Services/NotificationFeed.cs  đọc / bỏ thông báo của Windows
  Services/WifiNetworks.cs      quét, kết nối, quên mạng Wi-Fi (wlanapi)
  Services/BluetoothDevices.cs  thiết bị Bluetooth, ghép đôi;  BluetoothAudioLink.cs nối tai nghe
  Interop/TaskbarTracker.cs     vị trí taskbar (cả taskbar dọc), dò khoảng trống bằng UI Automation
  Interop/XamlTaskbar.cs        nạp module làm trong suốt taskbar Windows 11
  Interop/TrayMerge.cs          ẩn icon âm lượng / chuông, dồn khay, khôi phục
  Views/SeekRenderer.cs         14 kiểu thanh tiến trình, 4 hướng chạy
  Views/LyricsPresenter.cs      lời bài hát và các hiệu ứng;  WordStackPresenter.cs cho taskbar dọc
  Views/TaskbarOverlay.cs       điều phối: xếp lớp, màu, lời, menu
  Views/TaskbarOverlays.cs      một overlay cho mỗi taskbar được chọn
  Views/Face/                   cái mặt: FaceBrain (tâm trạng), FaceRig (chuyển động), FacePainters (8 kiểu)
src/MusicBar.TaskbarTap/        module C++ chạy trong Explorer (build bằng MSVC cùng app)
installer/                      Inno Setup + build.ps1
promo/                          video giới thiệu (HyperFrames)
```

## Video giới thiệu

Video 52 giây trong [release](https://github.com/lvt1693/music-bar/releases/latest): một thanh
taskbar ngủ gật, kèn buồn, tiếng cào đĩa, rồi sân khấu bật đèn. Toàn bộ phần MusicBar trong
video là chính code của app vẽ ra (`promo/tools/capture` gọi thẳng SeekRenderer,
LyricsPresenter và bảng điều khiển), phần còn lại là HTML dựng bằng
[HyperFrames](https://hyperframes.heygen.com). Giọng đọc từ queen-tts, nhạc nền và tiếng động
từ ElevenLabs. Muốn dựng lại: vào `promo/`, `npx hyperframes preview`.

## Nói thật

- Mới chạy kỹ trên Windows 11 (bản 26300), cả taskbar gốc lẫn taskbar ExplorerPatcher.
  Windows 10 có code cho nó, icon và phông đều có bản dự phòng sẵn trên Windows 10, nhưng
  chưa thử trên máy Windows 10 thật. Dùng Windows 10 mà gặp lỗi thì gửi giúp file
  `%LOCALAPPDATA%\MusicBar\musicbar.log`.
- Ô Phụ đề trực tiếp chỉ có từ Windows 11 22H2, máy cũ hơn sẽ không thấy ô này.
- Module taskbar Windows 11 nằm trong Explorer tới khi Explorer khởi động lại, kể cả sau khi
  gỡ MusicBar. Nó không làm gì khi MusicBar không chạy. Phần mềm diệt virus có thể để ý
  chuyện một DLL được nạp vào Explorer.
- Chưa có chữ ký số, nên SmartScreen sẽ hỏi lần đầu.
- Nhiều màn hình mới thử theo log của người dùng, máy làm app chỉ có một màn. Taskbar dọc đã
  chạy thật trên máy thử (dựng bên trái).
- Danh sách Bluetooth chưa được thử với thiết bị thật (máy thử không có Bluetooth). Phần kết
  nối / ngắt tai nghe gửi lệnh thẳng cho driver âm thanh Bluetooth, vì Windows không có API
  công khai cho việc này. Gặp lỗi thì gửi giúp file log.
- Trên Windows 11, nút mạng / âm lượng được nhận ra qua tên hiển thị (Windows tiếng Anh và
  tiếng Việt).
- Ô Ánh sáng đêm ghi vào một giá trị CloudStore không có tài liệu (định dạng do cộng đồng
  giải mã). Gặp trường lạ là MusicBar không ghi.
- Click lên taskbar thì Windows kéo taskbar lên trên cùng. MusicBar đặt lại thứ tự ngay, và
  kiểm tra lại mỗi 250 ms.

## Cảm ơn

[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) đã chỉ đường vào nền XAML của
taskbar Windows 11. [LRCLIB](https://lrclib.net) cho lời bài hát.
[ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) giữ taskbar kiểu cũ sống tới giờ.
Phông chữ từ [Google Fonts](https://fonts.google.com). Installer dựng bằng
[Inno Setup](https://jrsoftware.org/isinfo.php).

## License

[MIT](LICENSE). Các phông chữ đi kèm dùng giấy phép SIL Open Font License; bản giấy phép nằm
trong `Assets/Fonts/licenses` cạnh app.

## English, briefly

MusicBar turns the Windows taskbar into a music bar. It sits under the taskbar (which stays
fully usable) and adds:

- a full-width progress bar in 14 styles that take their colours from the cover art and dance
  to the audio actually playing;
- cover art and transport buttons next to the tray;
- synced karaoke lyrics from LRCLIB, laid out in the taskbar's free space, with 10 bundled
  fonts that cover Vietnamese;
- its own taskbar transparency for Windows 11 (an in-process XAML diagnostics module, done the
  way TranslucentTB does it, written independently);
- a control center that gathers notifications, quick settings, volume and output devices, with
  Wi-Fi and Bluetooth lists (join, pair, connect, forget);
- taskbars on any edge (a vertical taskbar gets one word of the lyrics per line), several
  screens with separate choices for the bar and the player, four progress directions, and a
  minimal mode with just the bar (and the lyrics, if you like);
- a little face from the promo film living on the taskbar, in eight looks: it dances to the
  music, gets sleepy at night, and giggles, sulks, laughs or gets dizzy when you click, rub or
  scroll over it;
- light and dark backgrounds, following Windows' mode by default.

It follows any app that talks to Windows Media Session (Spotify, browsers, Media Player…).
Grab the installer or the portable zip from the
[latest release](https://github.com/lvt1693/music-bar/releases/latest). Both are
self-contained, so there is no .NET to install. The UI is in English by default, with
Vietnamese one click away in the settings window; the code and its comments are in English.
