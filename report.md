# SilverDasher.Weaver.dll 逆向工程报告

## 概述

**目标文件**: `D:\Games\ACT.DieMoe\Plugins\SilverDasher\libs\SilverDasher.Weaver.dll`  
**工具**: IDA Pro 9.x + Hex-Rays 反编译器  
**日期**: 2026-06-19  
**分析者**: 赵工 / WorkBuddy  

## 背景

SilverDasher Dalamud 插件无法接收到 MQTT 推送消息。根因是 `SilverDasher.Weaver.dll`（ACT 版原生产物）在 Dalamud 进程中加载失败（错误码 0x8007045A），导致认证签名（Judge/Seal）为空字符串，认证服务器拒绝请求，无法获取 MQTT 会话令牌。

## 导出函数清单

| 地址 | 原名 | 重命名后 | 功能 |
|------|------|---------|------|
| 0x180003DA0 | Judge | Judge_GetHddSerial | 获取硬盘序列号作为机器指纹 |
| 0x1800032C0 | Seal | Seal_MixPlayerSignature | 混合服务器名+角色名+机器指纹生成签名 |
| 0x180002E60 | Weave | Weave_InterleaveEncode | 两字符串交织编码 |
| 0x1800039A0 | Benison | Benison_AesCbcEncrypt | AES-128-CBC 加密 + Hex 编码 |

## 关键辅助函数

| 地址 | 原名 | 重命名后 | 功能 |
|------|------|---------|------|
| 0x180004580 | sub_180004580 | GenerateMachineFingerprint | 读取物理硬盘序列号（Judge 核心） |
| 0x180001B60 | sub_180001B60 | StdStringTrim | std::string 首尾空白裁剪 |
| 0x180001540 | sub_180001540 | StringFromCStr | 从 C 字符串构造 std::string |
| 0x180001670 | sub_180001670 | StringCopy | std::string 拷贝 |
| 0x1800011E0 | sub_1800011E0 | Base64Encode | 标准 Base64 编码 |
| 0x180002800 | sub_180002800 | AesCbcEncrypt | AES-128-CBC 加密（PKCS7 填充） |
| 0x180002610 | sub_180002610 | AesEncryptBlock | AES 单块加密 |
| 0x1800022B0 | sub_1800022B0 | AesKeyExpansion | AES 密钥扩展 |
| 0x180003E70 | sub_180003E70 | StringAssign | std::string 赋值 |

## 关键数据变量

| 地址 | 原名 | 重命名后 | 内容 |
|------|------|---------|------|
| 0x180009BD0 | xmmword_180009BD0 | g_AesKey_SilverDasherSeal | AES 密钥: `SilverDasherSeal` (16 bytes ASCII) |
| 0x180009CA0 | xmmword_180009CA0 | g_EmptyStringInit | 空 std::string 初始化常量 |
| 0x18000CAC0 | byte_18000CAC0 | g_HddSerialBuffer | 硬盘序列号缓冲区 |
| 0x18000C8B0 | unk_18000C8B0 | g_AtaIdentifyBuffer | ATA IDENTIFY 数据缓冲区 |
| 0x180009940 | aAbcdefghijklmn | g_Base64Alphabet | Base64 字母表 |

## 详细分析

### 1. Judge_GetHddSerial (0x180003DA0)

**功能**: 获取机器指纹（硬盘序列号）

**流程**:
1. 调用 `GenerateMachineFingerprint()` 填充 std::string
2. `strdup` 结果并返回

**GenerateMachineFingerprint (0x180004580) 内部流程**:
1. 循环 `i = 0..15`，尝试打开 `\\.\PhysicalDrive%d`
2. `CreateFileW(FileName, GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL)`
3. `DeviceIoControl(handle, 0x74080, NULL, 0, &OutBuffer, 0x18, ...)` — IOCTL_STORAGE_QUERY_PROPERTY
4. 如果设备有效（`BYTE3(OutBuffer)` 非零）:
   - 构造 ATA PASS THROUGH 请求（IOCTL 0x7C088）
   - 读取 ATA IDENTIFY 数据到 `g_AtaIdentifyBuffer`
5. `CloseHandle(handle)`
6. 如果以上全部失败，回退:
   - `CreateFileW("\\\\.\\PHYSICALDRIVE0", 0, ...)` — 只读模式
   - `DeviceIoControl(handle, 0x2D1400, &InBuffer, 0xC, &Size, 8, ...)` — IOCTL_DISK_GET_LENGTH_INFO
   - 如果成功，从偏移 `v17[6]`（第 7 个 DWORD）处读取序列号
7. 如果全部失败，返回 `"failed"`
8. 结果经 `StdStringTrim()` 裁剪后返回

**C# 等价实现**: 使用 `System.Management.ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMedia")` 或 P/Invoke `DeviceIoControl`。

### 2. Seal_MixPlayerSignature (0x1800032C0)

**功能**: 生成玩家签名

**参数**: `a1` = 服务器名（字节数组），`a2` = 角色名（字节数组）

**流程**:
1. 将 a1、a2 转为 std::string
2. 调用 `GenerateMachineFingerprint()` 获取 Judge（硬盘序列号）
3. 全部 trim
4. 计算最大长度: `maxLen = max(len(server), len(player), len(judge))`
5. 逐字节混合:
   ```
   for i in 0..maxLen:
       result[i] = (server[i % serverLen] + player[i % playerLen] + judge[i % judgeLen]) / 3
       if result[i] == 0:
           result[i] = 0xCA  // 202, 防止字符串截断
   ```
6. 将结果转为 std::string
7. 调用 `StringCopy` + `StdStringTrim`
8. **过滤字符**: 移除所有 `/` (0x2F=47) 和 `\\` (0x5C=92) 字符
9. 返回结果

**关键**: 这是简单的三路字节混合，不是加密。除以 3 取整数部分。

### 3. Weave_InterleaveEncode (0x180002E60)

**功能**: 两字符串交织编码

**参数**: `a1` = 字节数组 A，`a2` = 字节数组 B

**流程**:
1. 将 a1、a2 转为 std::string，trim
2. 获取 Judge 指纹，trim
3. 拼接: `concat = b + judge`（B 在前，Judge 在后）
4. 逐字节加法:
   ```
   for i in 0..len(concat):
       sum = concat[i] + a[i % len(a)]
       if sum > 127:
           result[i] = sum + 1  // 溢出处理
       else:
           result[i] = sum
   ```
5. 交织重排:
   ```
   front = 0, back = len-1
   for i in 0..len/2:
       output[2*i]   = result[front++]
       output[2*i+1] = result[back--]
   if len is odd:
       output[len-1] = result[front]
   ```
6. 进一步处理（StringFromCStr + StdStringTrim）
7. 返回结果

### 4. Benison_AesCbcEncrypt (0x1800039A0)

**功能**: AES-128-CBC 加密并输出 Hex 字符串

**参数**: `a1` = 输入字符串（字节数组）

**常量**:
- **AES 密钥**: `SilverDasherSeal` (16 bytes, 位于 g_AesKey_SilverDasherSeal @ 0x180009BD0)
- **IV**: `0123456789abcdef` (16 bytes, 硬编码在栈上)
- **Hex 查找表**: `0123456789abcdef`

**流程**:
1. 将输入转为 std::string
2. 分配 16 字节对齐的缓冲区
3. 加载 AES 密钥到分配的内存
4. 设置 IV = "0123456789abcdef"
5. 调用 `AesCbcEncrypt(plaintext, plaintextLen, IV, key, output, outputSize)`
   - 内部使用 PKCS7 填充
   - CBC 模式：每块与前一块密文 XOR
6. 将密文逐字节转为 Hex 字符串（使用 "0123456789abcdef" 查找表）
7. 返回 Hex 字符串

## 认证协议

### 请求

```
POST https://nest.garlandtools.cn/wake
Content-Type: application/x-www-form-urlencoded

Body: https://nest.garlandtools.cn/wake&i={judge}&n={playerName}&s={worldId}&v={seal}&ve={version}
```

**参数说明**:
- `i` (judge): `Judge_GetHddSerial()` 返回值 — 硬盘序列号
- `n` (name): 角色名称
- `s` (server): 世界 ID (uint)
- `v` (seal): `Seal_MixPlayerSignature(serverName, playerName)` 返回值
- `ve` (version): 393220 (0x60004)

### 响应

| 响应 | 含义 |
|------|------|
| 16 字符字符串 | 认证成功，返回 MQTT session token |
| "Banned" | 已封禁 |
| "Left" | 永久封禁 |
| "Expired" | 版本过期 |
| "Blocked..." | 临时封禁 |

### 服务器状态

`https://nest.garlandtools.cn/wake` 当前可达（HTTP 403 — 需要 POST 参数）。

## 根因分析

```
Weaver.dll 加载失败 (0x8007045A)
    ↓
Judge() 和 Seal() 返回空字符串
    ↓
认证请求缺少有效签名 (i="", v="")
    ↓
认证服务器拒绝
    ↓
无 MQTT session token
    ↓
MQTT 连接无凭证
    ↓
无法接收推送消息
```

## 解决方案

将 Weaver.dll 的 4 个函数用 C# 托管代码重新实现，无需加载原生 DLL：

1. **Judge**: 使用 WMI 或 P/Invoke `DeviceIoControl` 读取硬盘序列号
2. **Seal**: 三路字节混合 `(a+b+c)/3`，过滤 `/` 和 `\\`
3. **Weave**: 逐字节加法 + 交织重排
4. **Benison**: `System.Security.Cryptography.AesManaged` + CBC 模式

### AES 密钥

**密钥**: `SilverDasherSeal`  
**IV**: `0123456789abcdef`

## IDA 修改记录

### 函数重命名（13 项）

| 地址 | 原名 | 新名 |
|------|------|------|
| 0x180003DA0 | Judge | Judge_GetHddSerial |
| 0x1800032C0 | Seal | Seal_MixPlayerSignature |
| 0x180002E60 | Weave | Weave_InterleaveEncode |
| 0x1800039A0 | Benison | Benison_AesCbcEncrypt |
| 0x180004580 | sub_180004580 | GenerateMachineFingerprint |
| 0x180001B60 | sub_180001B60 | StdStringTrim |
| 0x180001540 | sub_180001540 | StringFromCStr |
| 0x180001670 | sub_180001670 | StringCopy |
| 0x1800011E0 | sub_1800011E0 | Base64Encode |
| 0x180002800 | sub_180002800 | AesCbcEncrypt |
| 0x180002610 | sub_180002610 | AesEncryptBlock |
| 0x1800022B0 | sub_1800022B0 | AesKeyExpansion |
| 0x180003E70 | sub_180003E70 | StringAssign |

### 数据重命名（5 项）

| 地址 | 原名 | 新名 |
|------|------|------|
| 0x180009BD0 | xmmword_180009BD0 | g_AesKey_SilverDasherSeal |
| 0x180009CA0 | xmmword_180009CA0 | g_EmptyStringInit |
| 0x18000CAC0 | byte_18000CAC0 | g_HddSerialBuffer |
| 0x18000C8B0 | unk_18000C8B0 | g_AtaIdentifyBuffer |
| 0x180009940 | aAbcdefghijklmn | g_Base64Alphabet |

### 注释添加（8 项）

已在上述所有关键函数和数据地址处添加详细注释。

### IDB 保存

已保存到: `D:\Games\ACT.DieMoe\Plugins\SilverDasher\libs\SilverDasher.Weaver.dll.i64`

## 下一步

1. 在 SilverDasher 项目中用 C# 重新实现 Judge/Seal/Benison
2. 移除 TailorHelper 的 P/Invoke 依赖
3. 测试认证流程
4. 验证 MQTT 连接和消息接收
