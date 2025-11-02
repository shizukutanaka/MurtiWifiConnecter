# MurtiWiFi Connector Improvement Backlog


### Comprehensive Improvement Review / 包括的な改善レビュー (2025-10-08)
- **Security Foundations / セキュリティ基盤**
  - **[JP]** `Core/SecurityManager.cs` と `Core/CommandExecution.cs` を対象に、ゼロトラスト運用と継続的脅威監視に向けた検証シナリオとテレメトリ収集を拡張します。
  - **[EN]** Extend zero-trust enforcement scenarios and continuous threat telemetry across `Core/SecurityManager.cs` and `Core/CommandExecution.cs`.
- **Operational Resilience / 運用レジリエンス**
  - **[JP]** `Core/SystemHealthMonitor.cs` の自己診断とフォールバック経路を整理し、構成ドリフト検知と自動復旧を意識した監視コマンドを追加します。
  - **[EN]** Harden `Core/SystemHealthMonitor.cs` diagnostics with drift detection and automated recovery pathways.
- **Performance Focus / パフォーマンス重点**
  - **[JP]** `Core/NetworkOperations.cs` と `Core/OptimizedWifiScanner.cs` における非同期処理の粒度を調整し、構成キャッシュヒット率とプロファイル整合性検証の高速化を図ります。
  - **[EN]** Tune async granularity in `Core/NetworkOperations.cs` and `Core/OptimizedWifiScanner.cs`, improving cache hit ratios and integrity validation throughput.
- **Automation Discipline / 自動化規律**
  - **[JP]** `Core/AutomationEngine.cs` のルール検証、実行キュー管理、バックプレッシャー制御を整理し、監査証跡との連携を強化します。
  - **[EN]** Refine rule validation, queue governance, and backpressure handling within `Core/AutomationEngine.cs`, tightening audit synchronization.
- **Billing & Licensing / 課金・ライセンス**
  - **[JP]** Stripeサブスクリプション導入を前提に、環境変数によるAPIキー管理、Webhook検証、製品エディション判定を設計し、CLIコマンドを追加します。
  - **[EN]** Prepare Stripe subscription enablement with environment-based API key management, webhook validation, edition gating, and dedicated CLI commands.
- **Documentation Experience / ドキュメント体験**
  - **[JP]** `docs/` 配下に多言語対応の利用ガイド、課金運用手順、バックログ管理方針を整理し、社内外共有を容易にします。
  - **[EN]** Curate multilingual user guides, billing operations playbooks, and backlog governance notes under `docs/` for enterprise distribution.


### Recently Completed Improvements (2025-09-27)

### Configuration Safety and Discoverability
- Hardened input validation paths across `Core/InputValidator.cs`, `Core/ConfigManager.cs`, and `Core/NetworkOperations.cs`, ensuring SSID・パスワード・ファイルパス・優先ネットワークが正規化・検証されるようになりました。
- Added configuration normalization during load/save/import to clamp numeric ranges, sanitize preferred networks, and reset invalid log/security values with clear console warnings。
- Introduced CLI feedback for `config set`, returning success/失敗状態と詳細メッセージを表示します。
- Added metadata registry and CLI commands (`config describe`, `config list`, `config metadata`) for documenting 設定情報を自動抽出できるようにしました。
- Implemented centralized CLI argument sanitization via `Core/CommandArgumentGuard.cs`, blocking unsafe tokens before dispatch and recording audit entries for rejected invocations。
- Enforced restrictive ACLs on credential storage via `Core/SecurityManager.cs`, granting access only to current user, Administrators, and LocalSystem while logging enforcement failures。
- Added secure temporary profile handling by routing all netsh profile imports through `SecurityManager.CreateSecureTempFileAsync()` and `SecurityManager.SecureDeleteFileAsync()`。
- Introduced `Core/ProfileXmlValidator.cs` to enforce W3C schema validation on generated・復元されるWi-FiプロファイルXMLを検証し、異常時には即座に遮断します。
- Implemented credential integrity digests in `Core/SecurityManager.cs`, persisting SHA256 checksums, auditing tamper detections, and purging digest metadata during lifecycle operations。
- Added backup digest enforcement in `Core/ProfileManager.cs`, writing `.sha256` files, verifying before restore, and logging audit events on mismatch。
- Added log integrity HMACs in `Core/Logger.cs`, generating per-run keys, persisting `.hmac` digests, and providing `VerifyLogIntegrityAsync()` for tamper detection。
- Refactored log integrity management in `Core/Logger.cs` to reuse `SecurityManager.GetIntegrityKeyAsync()` and apply restrictive ACLs to log files and digests immediately。
- Secured backup artifacts in `Core/ProfileManager.cs` by applying restrictive ACLs to `.json` and `.sha256` files immediately after creation。
- Enforced user configuration integrity verification in `Core/ConfigManager.cs`, quarantining tampered `user_config.json` files, refreshing cache state, and auditing remediation steps。
- Hardened logger initialization in `Core/Logger.cs` by applying secure ACLs to the log directory/files before writes and capturing audit events on enforcement failures。
- **Password validation**: Enhanced password complexity checking
- **Configuration consolidation**: Merged settings.json into config.json

### Code Quality
- **Syntax error fixes**: Corrected malformed ShowHelp method in WiFiTool.cs
- **Process I/O resilience**: Introduced SafeReadStreamAsync() to prevent netsh output failures
- **Temporary file lifecycle**: Implemented TryDeleteTempFile() with secure deletion

## High Priority Security Improvements

### 1. Credential Handling Pipeline Security
- Implement Windows Credential Manager integration for secure password storage
- Add comprehensive input validation and sanitization for all user inputs
- Implement process sandboxing for netsh execution
- Add detailed security event logging with audit trail
- Status: **Partially Implemented**

### 2. Profile XML Integrity Verification
- Enhance XML validation with W3C schema verification
- Implement digital signatures for profile files using X509 certificates
- Add tamper detection with cryptographic checksums
- Secure temporary file handling with restricted ACLs
--- Status: **Partially Implemented** (SHA256 verification added; secure credential ACL enforcement, protected temp file handling, and schema validation in place)

### 3. Command Argument Sanitization
- Implement strict regex-based input validation for all CLI arguments
- Add protection against command injection and path traversal attacks
- Sanitize SSID and password with character whitelisting
- Validate and normalize all file paths and network names
- Status: **In Progress** (Guard for dangerous shell tokens completed; extend coverage to path normalization and contextual allowlists)

## Medium Priority Performance Improvements

### 4. Network Scanning Optimization
- Implement intelligent caching with adaptive TTL based on network stability
- Optimize netsh command batching to reduce process overhead
- Add background scanning with event-driven updates
- Reduce memory footprint using object pooling
- Status: **Partially Implemented** (Basic caching added)

### 5. Connection Management Efficiency
- Implement connection pooling for frequent operations
- Add retry logic with exponential backoff and jitter
- Optimize profile creation using bulk operations
- Enhance signal strength monitoring with moving averages
- Status: **Planned**

### 6. Async Operations Enhancement
- Convert all I/O operations to true async/await patterns
- Implement cancellation tokens for all long-running operations
- Add timeout handling with configurable thresholds
- Optimize Task scheduling with custom TaskScheduler
- Status: **Planned**

## User Experience Enhancements

### 7. Interactive Mode Improvements
- Add command history with readline support
- Implement tab completion for commands and network names
- Add progress indicators using IProgress<T> pattern
- Enhance error messages with suggested fixes and documentation links
- Status: **Planned**

### 8. Configuration Management
- Implement JSON schema validation for configuration files
- Add configuration migration for version upgrades
- Support environment-based configuration profiles
- Add configuration hot-reload without restart
- Status: **Partially Implemented** (Files consolidated)

### 9. Logging and Diagnostics
- Implement structured logging with Serilog integration
- Add performance metrics collection with ETW
- Implement debug mode with verbose output
- Add diagnostic command for troubleshooting
- Status: **Partially Implemented** (CLI diagnostics available; additional telemetry pending)

## Technical Debt Reduction

### 10. Code Refactoring
- Extract common patterns into utility classes
- Implement dependency injection for better testability
- Reduce method complexity (target cyclomatic complexity < 10)
- Consolidate error handling into centralized middleware
- Status: **In Progress**

### 11. Resource Management
- Implement proper IDisposable patterns throughout
- Add using statements for all disposable resources
- Implement weak references for cache entries
- Add resource leak detection in debug builds
- Expose secure deletion helper for shared use across modules
- Status: **In Progress**

### 12. Testing Infrastructure
- Add unit tests with 80% code coverage target
- Implement integration tests for network operations
- Add performance benchmarks with BenchmarkDotNet
- Implement continuous testing with file watchers
- Status: **Planned**

## Documentation Improvements

### 13. User Documentation
- Create comprehensive user guide with screenshots
- Add troubleshooting section with common issues
- Create video tutorials for common operations
- Translate documentation to multiple languages
- Status: **In Progress**

### 14. Developer Documentation
- Add XML documentation comments to all public APIs
- Create architecture decision records (ADRs)
- Document build and deployment procedures
- Add contribution guidelines and code style guide
- Status: **Planned**

## Implementation Priority Matrix

| Priority | Category | Items | Target |
|----------|----------|-------|--------|
| Critical | Security | 1-3 | Next Release |
| High | Performance | 4-6 | Q1 2025 |
| Medium | UX | 7-9 | Q2 2025 |
| Low | Technical Debt | 10-12 | Q3 2025 |
| Ongoing | Documentation | 13-14 | Continuous |

## Risk Assessment

### Known Issues
- Profile creation may fail with special characters in SSID
- Connection history file grows unbounded
- Cache cleanup timer may cause memory pressure
- No rollback mechanism for failed operations

### Mitigation Strategies
- Implement comprehensive input validation
- Add file rotation for history logs
- Optimize cache eviction algorithm
- Add transaction support for critical operations

## Next Steps

1. Complete command argument sanitization
2. Implement structured logging framework
3. Add comprehensive error handling
4. Optimize async/await patterns
5. Create automated test suite

## Version History

- 1.0.0: Initial release with basic functionality
- 1.1.0: Security hardening and performance improvements
- 1.2.0: (Planned) Enhanced UX and configuration management
- v2.0.0: (Future) Full async rewrite with plugin architecture