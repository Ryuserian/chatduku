using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatDuku.Configuration;

namespace ChatDuku.Remote
{
    /// <summary>하트비트 생존 상태.</summary>
    public enum HeartbeatState { Idle, Connecting, Alive, Dead }

    /// <summary>
    /// OBS WebSocket 프로토콜 계열 — OBS 버전에 따라 대화 방식이 완전히 다르다.
    /// - V5: obs-websocket 5.x (OBS Studio 28+) — opcode 기반(0 Hello → 1 ServerHello → 2 Identify → 3 Identified → 5 Request → 6 RequestResponse)
    /// - V4: obs-websocket 4.x (OBS Studio 27 이하) — requestType/messageType 기반 순수 JSON, 핸드셰이크 없음
    /// </summary>
    public enum ObsProtocol { Unknown, V5, V4 }

    /// <summary>감시 대상 한 대(IP:Port)의 상태 데이터.</summary>
    public sealed class ObsTarget
    {
        public ObsTarget(string host, int port)
        {
            Host = host;
            Port = port;
        }

        public string Host { get; }
        public int Port { get; }
        public string? Password { get; set; }

        public HeartbeatState State { get; internal set; } = HeartbeatState.Idle;
        public ObsProtocol Protocol { get; internal set; } = ObsProtocol.Unknown;
        public string? WsVersion { get; internal set; }   // obs-websocket 버전 (예: 5.3.1)
        public string? ObsVersion { get; internal set; }   // OBS Studio 버전 (예: 30.1.2)

        // ===== v5 op 맵 캘리브레이션 (실서버 관측 기반으로 자동 조정) =====
        // 공식 맵: Hello=0(server push 아님·클라이언트) ServerHello=1 Identify=2 Identified=3 Event=4 Request=5 RequestResponse=6
        // 서버선행 관측 변형: ServerHello=0 Identify=1 Identified=2 Event=3 Request=4 RequestResponse=5
        /// <summary>클라이언트가Identify를 전송할 op (공식=2).</summary>
        public int IdentifyOp { get; internal set; } = 2;
        /// <summary>서버 Identified 응답으로 받을 op (공식=3).</summary>
        public int IdentifiedOp { get; internal set; } = 3;
        /// <summary>요청(Request) op (공식=5).</summary>
        public int RequestOp { get; internal set; } = 5;
        /// <summary>요청응답(RequestResponse) op (공식=6).</summary>
        public int ResponseOp { get; internal set; } = 6;
        /// <summary>Salt (서버 push에서).</summary>
        public string? Salt { get; internal set; }
        /// <summary>Challenge (서버 push에서, 공식은 없고 변형에서 사용).</summary>
        public string? Challenge { get; internal set; }
        /// <summary>비밀번호 후보 인덱스 — 인증 실패 시마다 +1하여 재도전 (단일 비밀번호로 다중 공식 시도).</summary>
        public int AuthCandidateIndex { get; internal set; }
        public int ConsecutiveFailures { get; internal set; }
        public string? LastError { get; internal set; }
        public DateTime? LastAliveUtc { get; internal set; }

        public Uri Uri => new($"ws://{Host}:{Port}");

        /// <summary>리모컨 상태 줄용 한 줄 요약.</summary>
        public string Describe()
        {
            string ver = WsVersion is null ? "" : $" · WS {WsVersion}";
            return State switch
            {
                HeartbeatState.Alive => $"❤ {Host}{ver}",
                HeartbeatState.Dead => LastAliveUtc is null
                    ? $"✖ {Host} — {LastError ?? "무응답"}"
                    : $"✖ {Host} — {LastError ?? "무응답"} · {(int)(DateTime.UtcNow - LastAliveUtc.Value).TotalSeconds}초 전엔 생존",
                HeartbeatState.Connecting => $"… {Host} 연결 시도 중",
                _ => $"{Host} 대기",
            };
        }

        /// <summary>툴팁용 상세 정보.</summary>
        public string Detail
        {
            get
            {
                string alive = LastAliveUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
                string proto = Protocol switch
                {
                    ObsProtocol.V5 => "5.x",
                    ObsProtocol.V4 => "4.x",
                    _ => "미확인",
                };
                return $"OBS Studio: {ObsVersion ?? "?"} · obs-websocket: {WsVersion ?? "?"} · " +
                       $"프로토콜: {proto}\n" +
                       $"마지막 생존: {alive} · 연속 실패: {ConsecutiveFailures}" +
                       (LastError is null ? "" : $"\n오류: {LastError}");
            }
        }
    }

    /// <summary>
    /// 다른 PC들의 OBS Studio를 WebSocket(기본 4455)으로 생존 감시하는 하트비트 모니터.
    /// - v5(OBS 28+) 핸드셰이크가 응답 없으면 v4(OBS 27 이하) 메시지 세트로 자동 폴백
    /// - 연결 실패/무응답이 임계값 넘으면 Dead 판정 → StatusChanged 이벤트로 알림
    /// - 중지 전까지 주기적으로 자동 재접속, 살아나면 Alive 전환(역시 이벤트)
    /// 리모컨 UI가 호출하며, 이벤트 핸들러는 호출자 쪽에서 Dispatcher로 마샬링한다.
    /// </summary>
    public sealed class ObsHeartbeatMonitor
    {
        private readonly List<ObsTarget> _targets = new();
        private readonly SemaphoreSlim _poke = new(0);
        private CancellationTokenSource? _cts;

        /// <summary>어떤 대상의 상태가 바뀔 때마다 발생(백그라운드 스레드).</summary>
        public event Action? StatusChanged;

        public IReadOnlyList<ObsTarget> Targets
        {
            get { lock (_targets) return _targets.ToArray(); }
        }

        public bool IsRunning => _cts is not { IsCancellationRequested: true };

        public bool AnyDead => Targets.Any(t => t.State == HeartbeatState.Dead);

        public string DeadHosts() =>
            string.Join(", ", Targets.Where(t => t.State == HeartbeatState.Dead).Select(t => t.Host));

        /// <summary>
        /// 감시를 시작한다. 기존 감시는 중단되고 새 목록으로 교체된다.
        /// endpoint별 Password가 있으면 그 값, 없으면 defaultPassword를 인증에 사용한다.
        /// </summary>
        public void Start(IEnumerable<(string Host, int Port, string? Password)> endpoints, string? defaultPassword)
        {
            Stop();

            lock (_targets)
            {
                _targets.Clear();
                foreach (var (host, port, password) in endpoints)
                    _targets.Add(new ObsTarget(host, port) { Password = string.IsNullOrEmpty(password) ? defaultPassword : password });
            }

            while (_poke.CurrentCount > 0) _poke.Wait(0); // 이전 세션의 '즉시' 요청 소진

            var cts = new CancellationTokenSource();
            _cts = cts;

            foreach (var t in Targets)
                _ = Task.Run(() => RunTargetLoopAsync(t, cts.Token));

            Raise();
        }

        /// <summary>감시를 중단하고 모든 대상 상태를 Idle로 돌린다.</summary>
        public void Stop()
        {
            var cts = _cts;
            _cts = null;
            try { cts?.Cancel(); cts?.Dispose(); } catch { /* 이미 종료됨 */ }

            foreach (var t in Targets)
                t.State = HeartbeatState.Idle;

            Raise();
        }

        /// <summary>"지금 확인" — 주기 대기 없이 즉시 하트비트를 돌린다.</summary>
        public void Poke() => _poke.Release(Math.Max(1, Targets.Count));

        // ===== 대상별 루프 =====

        private async Task RunTargetLoopAsync(ObsTarget t, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetState(t, HeartbeatState.Connecting);
                    t.Protocol = ObsProtocol.Unknown;

                    using var ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                    using (var connCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        connCts.CancelAfter(AppSettings.ObsConnectTimeoutMs);
                        await ws.ConnectAsync(t.Uri, connCts.Token);
                    }

                    // 버전별 프로토콜 자동 탐지(5.x → 4.x 폴백)
                    using (var hsCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        hsCts.CancelAfter(AppSettings.ObsResponseTimeoutMs * 2);
                        t.Protocol = await HandshakeAsync(ws, t, hsCts.Token);
                    }

                    t.ConsecutiveFailures = 0;
                    t.LastAliveUtc = DateTime.UtcNow;
                    SetState(t, HeartbeatState.Alive);

                    // 주기적 생존 확인 — 주기 대기 중 '즉시(Poke)' 오면 바로 동작
                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        await _poke.WaitAsync(TimeSpan.FromMilliseconds(AppSettings.ObsHeartbeatIntervalMs), ct);

                        if (await PulseAsync(ws, t))
                        {
                            t.ConsecutiveFailures = 0;
                            t.LastAliveUtc = DateTime.UtcNow;
                            SetState(t, HeartbeatState.Alive);
                        }
                        else if (++t.ConsecutiveFailures >= AppSettings.ObsDeadThreshold)
                        {
                            SetState(t, HeartbeatState.Dead);
                            break; // 재접속 루프로
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    t.LastError = Friendly(ex);

                    // 인증 계열 실패 → 다음 재접속 때 다른 비밀번호 공식 후보로 자동 재도전
                    if (t.LastError is not null &&
                        (t.LastError.Contains("인증") || t.LastError.Contains("uthentication")))
                        t.AuthCandidateIndex++;

                    t.ConsecutiveFailures++;
                    SetState(t, t.ConsecutiveFailures >= AppSettings.ObsDeadThreshold
                        ? HeartbeatState.Dead
                        : HeartbeatState.Connecting);
                }

                try { await Task.Delay(AppSettings.ObsReconnectDelayMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ===== 프로토콜 핸드셰이크 (op 맵 자동 캘리브레이션) =====
        //
        // obs-websocket 5.x는 서버 구현마다 악수 순서/op가 다르다(실관측):
        //  - 스펙 v5       : 클라이언트 Hello(op:0) → 서버 ServerHello(op:1) → Identify(2)/Identified(3)/Request(5)/Response(6)
        //  - 서버선행 변형 : 서버가 연결 즉시 ServerHello를 op:0로 push → Identify(1)/Identified(2)/Request(4)/Response(5)  [OBS 32.x 실측]
        //  - v4           : push 없음, requestType 기반 순수 JSON
        // 첫 메시지를 먼저 수신해哪一种인지 판정하고 op 맵을 캘리브레이션한다.

        private static async Task<ObsProtocol> HandshakeAsync(ClientWebSocket ws, ObsTarget t, CancellationToken ct)
        {
            // ① 먼저 서버 push를 잡는다 — push를 보내는 서버는 변형(또는 v4 이벤트)
            string? first = null;
            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(AppSettings.ObsHelloProbeMs);
                first = await ReceiveTextAsync(ws, probeCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                first = null; // push 없음 → 일반 순서
            }

            if (first is not null)
            {
                using var doc = JsonDocument.Parse(first);
                var root = doc.RootElement;
                int firstOp = TryInt(root, "op") ?? -1;

                if (root.TryGetProperty("d", out var helloD) && LooksLikeHelloPayload(helloD))
                {
                    // push가 ServerHello다: op:1 = 스펙 v5, op:0 = 서버선행 -1 오프셋 변형
                    return await CompleteV5Async(ws, t, helloD, shifted: firstOp == 0, ct);
                }

                // push가 hello가 아니다(이벤트류) → 소비했으니 v4 세트로 확인
                return await CompleteV4Async(ws, t, ct);
            }

            // ② push 없는 서버: 스펙대로 Hello(op:0) 전송 → ServerHello(op:1) 응답 대기
            try
            {
                await SendAsync(ws, """{"op":0,"d":{"rpcVersion":1}}""", ct);

                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(AppSettings.ObsHelloProbeMs);

                try
                {
                    string reply = await ReceiveTextAsync(ws, probeCts.Token);
                    using var doc = JsonDocument.Parse(reply);

                    if (TryInt(doc.RootElement, "op") == 1 &&
                        doc.RootElement.TryGetProperty("d", out var helloD))
                        return await CompleteV5Async(ws, t, helloD, shifted: false, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Hello에 응답이 없다 → v4 이하 서버라는 뜻
                }
            }
            catch (WebSocketException)
            {
                throw; // 연결 자체가 끊긴 경우 → 상위에서 실패 처리
            }

            // ③ v4(OBS 27 이하): 핸드셰이크 없이 GetVersion 요청/응답으로 확인
            return await CompleteV4Async(ws, t, ct);
        }

        private static async Task<ObsProtocol> CompleteV5Async(ClientWebSocket ws, ObsTarget t, JsonElement helloD, bool shifted, CancellationToken ct)
        {
            t.WsVersion = GetStr(helloD, "obsWebSocketVersion") ?? t.WsVersion;
            t.ObsVersion = GetStr(helloD, "obsStudioVersion") ?? t.ObsVersion;

            // op 맵 캘리브레이션: 스펙 vs 서버선행 -1 오프셋 변형
            t.IdentifyOp = shifted ? 1 : 2;
            t.IdentifiedOp = shifted ? 2 : 3;
            t.RequestOp = shifted ? 4 : 5;
            t.ResponseOp = shifted ? 5 : 6;

            // 인증 파라미터 추출: 스펙 = salt 문자열 / 변형 = {challenge, salt} 객체
            t.Salt = null;
            t.Challenge = null;
            bool hasAuthInfo = false;
            if (helloD.TryGetProperty("authentication", out var a))
            {
                if (a.ValueKind == JsonValueKind.String)
                {
                    t.Salt = a.GetString();
                    hasAuthInfo = !string.IsNullOrEmpty(t.Salt);
                }
                else if (a.ValueKind == JsonValueKind.Object)
                {
                    t.Salt = GetStr(a, "salt");
                    t.Challenge = GetStr(a, "challenge");
                    hasAuthInfo = t.Salt is not null || t.Challenge is not null;
                }
            }

            var reqs = GetStr(helloD, "authenticationRequirements");
            bool mandatory = hasAuthInfo ||
                (!string.IsNullOrEmpty(reqs) &&
                 !reqs.Equals("NotRequired", StringComparison.OrdinalIgnoreCase) &&
                 !reqs.Equals("Recommended", StringComparison.OrdinalIgnoreCase));

            if (mandatory && string.IsNullOrEmpty(t.Password))
            {
                t.LastError = "인증 필요 — 비밀번호 입력";
                throw new UnauthorizedAccessException(t.LastError);
            }

            string? auth = BuildAuthCandidate(t); // 이번 연결에서 쓸 비밀번호 공식 후보

            string identify = JsonSerializer.Serialize(new
            {
                op = t.IdentifyOp,
                d = new
                {
                    rpcVersion = 1,
                    authentication = auth,
                    clientProperties = new { name = AppSettings.AppDisplayName, version = "1.0" },
                },
            });
            await SendAsync(ws, identify, ct);

            // Identified 대기(캘리브레이션 op 또는 스펙 3 허용). 비밀번호가 틀면 서버가 소켓을 닫는다.
            string msg = await ReceiveTextAsync(ws, ct);
            using var doc = JsonDocument.Parse(msg);
            int op = TryInt(doc.RootElement, "op") ?? -1;
            if (op != t.IdentifiedOp && op != 3)
                throw new IOException($"예상치 못한 v5 응답(op={op}): {Truncate(msg)}");

            t.IdentifiedOp = op;
            t.AuthCandidateIndex = 0; // 인증 성공 — 이후 같은 공식 유지

            // Identified 성공 → GetVersion으로 OBS Studio 버전 수집(실패해도 생존 판정은 유지)
            await PulseAsync(ws, t);
            return ObsProtocol.V5;
        }

        /// <summary>서버 push의 d가 ServerHello다운지(버전/인증 필드 존재) 판별.</summary>
        private static bool LooksLikeHelloPayload(JsonElement d) =>
            d.TryGetProperty("obsWebSocketVersion", out _) ||
            d.TryGetProperty("authenticationRequirements", out _) ||
            d.TryGetProperty("authentication", out _);

        /// <summary>
        /// 인증 문자열 후보 생성. 서버 변형에 따라 다른 공식을 쓸 수 있어
        /// 실패할 때마다 AuthCandidateIndex를 돌려 재도전한다(재접속 필요 — 서버가 소켓을 닫으므로).
        /// </summary>
        private static string? BuildAuthCandidate(ObsTarget t)
        {
            if (string.IsNullOrEmpty(t.Password))
                return null;

            string salt = t.Salt ?? "";
            string secret = B64Sha256(t.Password! + salt); // b64(sha256(pw+salt)) — 공통 secret

            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(t.Challenge))
            {
                candidates.Add(B64Sha256(secret + t.Challenge));   // 챌린지 바인딩형
                candidates.Add(B64Sha256(t.Challenge! + secret));
            }
            candidates.Add(B64Sha256(secret + salt));               // 공식 v5
            candidates.Add(t.Password!);                           // 평문(v4 스타일)

            return candidates[t.AuthCandidateIndex % candidates.Count];
        }

        private static async Task<ObsProtocol> CompleteV4Async(ClientWebSocket ws, ObsTarget t, CancellationToken ct)
        {
            var (ok, err, wsVer, obsVer) = await GetVersionV4Async(ws, ct);

            // 4.x: 서버에 인증이 켜져 있으면 Authenticate부터 통과해야 다른 요청이 허용된다
            if (!ok && IsAuthIssue(err))
            {
                if (string.IsNullOrEmpty(t.Password))
                    throw new UnauthorizedAccessException("OBS(4.x) 인증 필요 — 비밀번호 입력");

                using var authResp = await RequestV4Async(ws, "Authenticate", new { password = t.Password }, ct);
                if (!TryBool(authResp.RootElement, "result"))
                    throw new UnauthorizedAccessException($"OBS 4.x 인증 실패 — {GetStr(authResp.RootElement, "error") ?? "비밀번호 확인"}");

                (ok, err, wsVer, obsVer) = await GetVersionV4Async(ws, ct);
            }

            if (!ok)
                throw new IOException(err ?? "OBS 4.x 응답 확인 실패 (프로토콜 미지원?)");

            t.WsVersion = wsVer ?? t.WsVersion;
            t.ObsVersion = obsVer ?? t.ObsVersion;
            return ObsProtocol.V4;
        }

        // ===== 하트비트 펄스 (GetVersion 왕복) =====

        private static async Task<bool> PulseAsync(ClientWebSocket ws, ObsTarget t)
        {
            try
            {
                using var cts = new CancellationTokenSource(AppSettings.ObsResponseTimeoutMs);
                string reqId = Guid.NewGuid().ToString("N");

                string req = t.Protocol == ObsProtocol.V5
                    ? JsonSerializer.Serialize(new { op = t.RequestOp, d = new { requestType = "GetVersion", requestId = reqId } })
                    : JsonSerializer.Serialize(new { requestType = "GetVersion", requestId = reqId });

                await SendAsync(ws, req, cts.Token);

                // 이벤트(op 4 / messageType Event)는 건너뛰며 내 requestId 응답만 기다린다
                while (true)
                {
                    string msg = await ReceiveTextAsync(ws, cts.Token);
                    using var doc = JsonDocument.Parse(msg);
                    var r = doc.RootElement;

                    if (t.Protocol == ObsProtocol.V5)
                    {
                        if (TryInt(r, "op") != t.ResponseOp) continue;
                        if (!r.TryGetProperty("d", out var d)) continue;
                        if (GetStr(d, "requestId") != reqId) continue;
                        if (!d.TryGetProperty("requestStatus", out var st)) continue;

                        int? code = TryInt(st, "code");
                        if (code.HasValue && code != 1)
                        {
                            t.LastError = GetStr(st, "comment") ?? "요청 거절";
                            return false;
                        }

                        if (d.TryGetProperty("requestData", out var rd))
                            ReadVersions(t, rd);
                        return true;
                    }
                    else
                    {
                        if (GetStr(r, "requestId") != reqId) continue;
                        if (!TryBool(r, "result"))
                        {
                            t.LastError = FriendlyV4Error(GetStr(r, "error"));
                            return false;
                        }

                        if (r.TryGetProperty("responseData", out var rd))
                            ReadVersions(t, rd);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                t.LastError = Friendly(ex);
                return false;
            }
        }

        // ===== v4 메시지 세트 =====

        private static async Task<(bool Ok, string? Err, string? WsVer, string? ObsVer)> GetVersionV4Async(
            ClientWebSocket ws, CancellationToken ct)
        {
            using var doc = await RequestV4Async(ws, "GetVersion", null, ct);

            var r = doc.RootElement;
            bool ok = TryBool(r, "result");

            string? wsVer = null, obsVer = null;
            if (r.TryGetProperty("responseData", out var rd))
            {
                wsVer = GetStr(rd, "obsWebsocketsVersion") ?? GetStr(rd, "obsWebSocketVersion");
                obsVer = GetStr(rd, "obsStudioVersion");
            }

            return (ok, ok ? null : FriendlyV4Error(GetStr(r, "error")), wsVer, obsVer);
        }

        private static async Task<JsonDocument> RequestV4Async(ClientWebSocket ws, string type, object? requestData, CancellationToken ct)
        {
            string reqId = Guid.NewGuid().ToString("N");
            string json = requestData is null
                ? JsonSerializer.Serialize(new { requestType = type, requestId = reqId })
                : JsonSerializer.Serialize(new { requestType = type, requestId = reqId, requestData });

            await SendAsync(ws, json, ct);

            // Event 등을 건너뛰며 같은 requestId의 RequestResponse를 기다린다
            while (true)
            {
                string msg = await ReceiveTextAsync(ws, ct);
                JsonDocument doc;
                try { doc = JsonDocument.Parse(msg); }
                catch (JsonException) { continue; }

                var r = doc.RootElement;
                if ((GetStr(r, "messageType") ?? "RequestResponse") == "RequestResponse" &&
                    (GetStr(r, "requestId") ?? reqId) == reqId)
                    return doc;

                doc.Dispose();
            }
        }

        // ===== 공통 유틸 =====

        private static Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct) =>
            ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

        private static async Task<string> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[8192];
            var sb = new StringBuilder();

            while (true)
            {
                var res = await ws.ReceiveAsync(buf, ct);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    // 종료 코드+사유를 메시지에 담아 인증 실패(4009) 등을 상위에서 분류할 수 있게 한다
                    int code = res.CloseStatus.HasValue ? (int)res.CloseStatus.Value : -1;
                    string desc = string.IsNullOrEmpty(res.CloseStatusDescription)
                        ? "" : $" — {res.CloseStatusDescription}";
                    throw new WebSocketException($"접속 종료({code}){desc}");
                }

                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                if (res.EndOfMessage) return sb.ToString();
            }
        }

        private static string B64Sha256(string s) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        private void SetState(ObsTarget t, HeartbeatState s)
        {
            if (t.State == s) return;

            // 이미 Dead: 재접속 시도를 '연결 중'으로 위장하지 않는다 — Alive 복구 전까지 Dead 유지
            if (t.State == HeartbeatState.Dead && s is HeartbeatState.Connecting or HeartbeatState.Idle)
                return;

            t.State = s;
            Raise();
        }

        private void Raise() => StatusChanged?.Invoke();

        private static void ReadVersions(ObsTarget t, JsonElement rd)
        {
            t.WsVersion = GetStr(rd, "obsWebSocketVersion") ?? GetStr(rd, "obsWebsocketsVersion") ?? t.WsVersion;
            t.ObsVersion = GetStr(rd, "obsStudioVersion") ?? t.ObsVersion;
        }

        private static bool IsAuthIssue(string? err) =>
            err is not null &&
            (err.Contains("uthentication", StringComparison.OrdinalIgnoreCase) ||
             err.Contains("authorized", StringComparison.OrdinalIgnoreCase) ||
             err.Contains("password", StringComparison.OrdinalIgnoreCase));

        private static string FriendlyV4Error(string? err)
        {
            if (string.IsNullOrEmpty(err)) return "무응답/거절";
            if (IsAuthIssue(err)) return "인증 필요 — 비밀번호 확인";
            return err;
        }

        private static string Friendly(Exception ex)
        {
            var b = ex.GetBaseException();

            if (ex is UnauthorizedAccessException || b is UnauthorizedAccessException)
                return "인증 실패 — 비밀번호 확인";

            if (b is SocketException se)
                return se.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "거부됨 — OBS WebSocket 꺼짐 확인",
                    SocketError.TimedOut => "연결 시간 초과",
                    SocketError.HostUnreachable => "도달 불가 — IP 확인",
                    SocketError.HostNotFound => "IP 확인 실패",
                    SocketError.NetworkUnreachable => "네트워크 경로 없음",
                    _ => $"네트워크 오류 ({se.SocketErrorCode})",
                };

            if (b is OperationCanceledException)
                return "타임아웃 — OBS 응답 없음";

            if (ex is WebSocketException we)
                return IsAuthIssue(we.Message)
                    ? "인증 실패 — 비밀번호 확인"
                    : "WebSocket 거부/단절";
            if (ex is IOException) return "접속 끊김";
            return b.GetType().Name;
        }

        private static string? GetStr(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static int? TryInt(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32()
                : null;

        private static bool TryBool(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static string Truncate(string s, int max = 80) =>
            s.Length <= max ? s : s[..max] + "…";
    }
}
