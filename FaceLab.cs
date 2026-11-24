using OpenCvSharp;
using OpenCvSharp.Face;

namespace FaceRecognition;

internal sealed class FaceLab : IDisposable
{
    private readonly CascadeClassifier detector;
    private readonly Dictionary<string, List<Mat>> samples = new(StringComparer.OrdinalIgnoreCase);
    private LBPHFaceRecognizer? recognizer;
    private string[] labels = [];
    internal bool Trained => recognizer is not null;
    internal string Summary => samples.Count == 0 ? "Henüz örnek yok." :
        string.Join(" · ", samples.Select(entry => $"{entry.Key}: {entry.Value.Count} örnek, {Math.Max(0, 3 - entry.Value.Count)} eksik"));
    internal bool Ready => samples.Count >= 2 && samples.Values.All(list => list.Count >= 3);

    internal FaceLab(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("OpenCV algılama modeli bulunamadı. README kurulumuna bak.", path);
        detector = new CascadeClassifier(path);
        if (detector.Empty())
        {
            detector.Dispose();
            throw new InvalidDataException("OpenCV algılama modeli okunamadı.");
        }
    }

    internal void AddSample(string label, Mat frame)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Önce kişi etiketi gir.");
        using var gray = ToGray(frame);
        var faces = Detect(gray);
        if (faces.Length != 1) throw new InvalidDataException("Örnek görüntüde tek bir yüz görünmeli.");
        using var crop = Normalize(gray, faces[0]);
        if (!samples.TryGetValue(label.Trim(), out var entries))
            samples.Add(label.Trim(), entries = []);
        if (entries.Any(existing => Cv2.Norm(existing, crop, NormTypes.L1) == 0))
            throw new InvalidDataException("Bu yüz örneği zaten eklendi. Farklı bir kare seç.");
        entries.Add(crop.Clone());
        recognizer?.Dispose();
        recognizer = null;
    }

    internal void Train()
    {
        if (!Ready) throw new InvalidOperationException("En az iki etiket için kişi başına üç farklı örnek ekle.");
        string[] names = samples.Keys.ToArray();
        var inputs = names.SelectMany(name => samples[name]).ToArray();
        var ids = names.SelectMany((name, id) => Enumerable.Repeat(id, samples[name].Count)).ToArray();
        var model = LBPHFaceRecognizer.Create(threshold: 85);
        try { model.Train(inputs, ids); }
        catch { model.Dispose(); throw; }
        recognizer?.Dispose();
        recognizer = model;
        labels = names;
    }

    internal (Bitmap Image, string Result) Render(Mat source, bool predict)
    {
        using var frame = source.Clone();
        using var gray = ToGray(source);
        var faces = Detect(gray);
        var results = new List<string>();
        foreach (var face in faces)
        {
            Cv2.Rectangle(frame, face, new Scalar(95, 215, 255), 2);
            if (!predict || recognizer is null) continue;
            using var crop = Normalize(gray, face);
            recognizer.Predict(crop, out int label, out double distance);
            results.Add(label < 0 ? "Bilinmeyen" : $"{labels[label]} (LBPH uzaklığı {distance:F1})");
        }
        Cv2.ImEncode(".png", frame, out byte[] bytes);
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        string result = faces.Length == 0 ? "Yüz algılanmadı." : $"{faces.Length} yüz algılandı.";
        if (predict && recognizer is null) result += " Tanıma modelini önce eğit.";
        if (results.Count > 0) result += " " + string.Join(" · ", results);
        return (new Bitmap(image), result);
    }

    private Rect[] Detect(Mat gray) => detector.DetectMultiScale(gray, 1.1, 5, minSize: new OpenCvSharp.Size(50, 50));

    private static Mat ToGray(Mat frame)
    {
        var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);
        return gray;
    }

    private static Mat Normalize(Mat gray, Rect face)
    {
        using var region = new Mat(gray, face);
        var resized = new Mat();
        Cv2.Resize(region, resized, new OpenCvSharp.Size(100, 100));
        return resized;
    }

    public void Dispose()
    {
        detector.Dispose();
        recognizer?.Dispose();
        foreach (var sample in samples.Values.SelectMany(list => list)) sample.Dispose();
    }
}
