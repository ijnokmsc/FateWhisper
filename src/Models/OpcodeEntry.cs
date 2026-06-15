using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// Opcode 条目模型，对应 opcodes.json 中的单条记录。
/// </summary>
public class OpcodeEntry
{
    /// <summary>
    /// Opcode 名称（如 "InitZone", "FateInfo", "ActorControlSelf"）。
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 国服 opcode 值（十六进制字符串，如 "0x0096"）。
    /// </summary>
    [JsonProperty("cn")]
    public string Cn { get; set; } = "";

    /// <summary>
    /// 国服包长度。
    /// </summary>
    [JsonProperty("cnl")]
    public int Cnl { get; set; }

    /// <summary>
    /// 国际服 opcode 值。
    /// </summary>
    [JsonProperty("global")]
    public string Global { get; set; } = "";

    /// <summary>
    /// 国际服包长度。
    /// </summary>
    [JsonProperty("globall")]
    public int Globall { get; set; }

    /// <summary>
    /// ActorControlSelf 的子类型映射（type → 事件名）。
    /// </summary>
    [JsonProperty("types")]
    public Dictionary<string, int>? Types { get; set; }

    /// <summary>
    /// 获取 uint16 格式的国服 opcode 值。
    /// 若 Cn 字段为空或无效，返回 0（调用方应通过判断 0 值来处理未配置的情况）。
    /// </summary>
    [JsonIgnore]
    public ushort OpcodeValue
    {
        get
        {
            if (string.IsNullOrEmpty(Cn))
            {
                return 0;
            }

            try
            {
                return Convert.ToUInt16(Cn, 16);
            }
            catch (FormatException)
            {
                return 0;
            }
            catch (OverflowException)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 获取 uint16 格式的国际服 opcode 值。
    /// 若 Global 字段为空或无效，返回 0。
    /// </summary>
    [JsonIgnore]
    public ushort OpcodeValueGlobal
    {
        get
        {
            if (string.IsNullOrEmpty(Global))
            {
                return 0;
            }

            try
            {
                return Convert.ToUInt16(Global, 16);
            }
            catch (FormatException)
            {
                return 0;
            }
            catch (OverflowException)
            {
                return 0;
            }
        }
    }
}
