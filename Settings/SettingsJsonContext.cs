using System.Text.Json.Serialization;

namespace EndfieldCharge.Settings;

/// <summary>
/// 设置文件的 JSON 源生成上下文。
///
/// 用源生成而不是运行期反射：序列化契约在编译期就被生成和检查，
/// 不需要运行期反射（可裁剪 / 可 AOT），启动也少一段反射预热。
///
/// NewLine 显式钉成 "\n"：.NET 9 起 JsonWriterOptions.NewLine 默认是
/// Environment.NewLine，也就是说在 Windows 上缩进 JSON 的换行会从 LF 变成 CRLF。
/// 那会让所有用户已有的 settings.json 无谓地变一遍字节，所以这里钉死成历史行为。
///
/// 注意：新增可序列化的类型时要在这里补一条 [JsonSerializable]，
/// 否则运行时会抛「metadata for type ... was not provided」。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, NewLine = "\n")]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
