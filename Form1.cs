using OpenCvSharp;

namespace FaceRecognition;

public partial class Form1 : Form
{
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(25, 23, 35) };
    private readonly TextBox label = new() { PlaceholderText = "Kişi etiketi", Width = 160, MaxLength = 40 };
    private readonly Label dataset = new() { Dock = DockStyle.Fill, Text = "Henüz örnek yok." };
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "Kamerayı başlat veya görüntü seç." };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private FaceLab? lab;
    private VideoCapture? camera;
    private Mat? current;
    private bool predict;

    public Form1()
    {
        InitializeComponent();
        Text = "Yüz Laboratuvarı";
        ClientSize = new System.Drawing.Size(900, 650);
        MinimumSize = new System.Drawing.Size(750, 500);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(241, 236, 251);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, Padding = new Padding(16) };
        grid.RowStyles.Add(new(SizeType.Absolute, 45));
        grid.RowStyles.Add(new(SizeType.Absolute, 42));
        grid.RowStyles.Add(new(SizeType.Percent, 100));
        grid.RowStyles.Add(new(SizeType.Absolute, 42));
        grid.RowStyles.Add(new(SizeType.Absolute, 46));
        grid.RowStyles.Add(new(SizeType.Absolute, 45));
        grid.Controls.Add(new Label { Text = "Yüz Laboratuvarı", Font = new Font("Segoe UI", 20, FontStyle.Bold), AutoSize = true });
        grid.Controls.Add(Row(Command("Kamera başlat", StartCamera), Command("Kamera durdur", () =>
        {
            StopCamera();
            status.Text = "Kamera durduruldu. Son kare ekranda.";
        }), Command("Görüntü seç", ChooseImage), Command("Algıla", () => ChangeMode(false)), Command("Tanı", () => ChangeMode(true))));
        grid.Controls.Add(preview);
        grid.Controls.Add(Row(label, Command("Görünen yüzü örnek ekle", AddSample), Command("Modeli eğit", Train)));
        grid.Controls.Add(dataset);
        grid.Controls.Add(status);
        Controls.Add(grid);
        timer.Tick += (_, _) => Guard(() =>
        {
            using var frame = new Mat();
            if (camera is null || !camera.Read(frame) || frame.Empty())
            {
                StopCamera();
                throw new IOException("Kameradan görüntü alınamadı.");
            }
            SetFrame(frame.Clone());
        });
        FormClosed += (_, _) =>
        {
            StopCamera();
            timer.Dispose();
            lab?.Dispose();
            current?.Dispose();
            preview.Image?.Dispose();
        };
    }

    private FaceLab Lab => lab ??= new FaceLab(Path.Combine(AppContext.BaseDirectory, "haarcascade_frontalface_default.xml"));

    private void StartCamera()
    {
        if (camera is not null) return;
        using var probe = new Mat();
        var device = new VideoCapture();
        try
        {
            if (!device.Open(0) || !device.Read(probe) || probe.Empty())
                throw new IOException("Kamera açılamadı. Bağlantıyı ve Windows kamera izinlerini kontrol et.");
            SetFrame(probe.Clone());
            camera = device;
            timer.Start();
        }
        catch { device.Dispose(); throw; }
    }

    private void StopCamera()
    {
        timer.Stop();
        camera?.Dispose();
        camera = null;
    }

    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog { Filter = "Görüntüler|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        StopCamera();
        var frame = Cv2.ImDecode(File.ReadAllBytes(dialog.FileName), ImreadModes.Color);
        if (frame.Empty())
        {
            frame.Dispose();
            throw new InvalidDataException("Görüntü dosyası okunamadı.");
        }
        SetFrame(frame);
    }

    private void SetFrame(Mat frame)
    {
        current?.Dispose();
        current = frame;
        try { Render(); }
        catch
        {
            Cv2.ImEncode(".png", frame, out byte[] bytes);
            using var stream = new MemoryStream(bytes);
            using var bitmap = Image.FromStream(stream);
            ShowImage(new Bitmap(bitmap));
            StopCamera();
            throw;
        }
    }

    private void ShowImage(Bitmap image)
    {
        var previous = preview.Image;
        preview.Image = image;
        previous?.Dispose();
    }

    private Mat Current => current ?? throw new InvalidOperationException("Önce kamera veya dosyadan bir görüntü aç.");

    private void Render()
    {
        var (image, result) = Lab.Render(Current, predict);
        ShowImage(image);
        status.Text = (predict ? "Tanıma · " : "Algılama · ") + result;
    }

    private void ChangeMode(bool useRecognition)
    {
        predict = useRecognition;
        Render();
    }

    private void AddSample()
    {
        Lab.AddSample(label.Text, Current);
        dataset.Text = Lab.Summary;
        status.Text = "Örnek eklendi. Veri değiştiği için modeli yeniden eğit.";
    }

    private void Train()
    {
        Lab.Train();
        predict = false;
        status.Text = "Model eğitildi. Eğitimde kullanılmamış bir görüntü seç ve Tanı düğmesine bas.";
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private Button Command(string caption, Action action)
    {
        var button = new Button { Text = caption, AutoSize = true };
        button.Click += (_, _) => Guard(action);
        return button;
    }

    private static Control Row(params Control[] entries)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill };
        row.Controls.AddRange(entries);
        return row;
    }
}
