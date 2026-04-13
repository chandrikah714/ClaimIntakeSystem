// ============================================================
// FILE: ClaimIntake.Tests/EncryptionServiceTests.cs
// PURPOSE: Tests for the AES encryption service.
//
// BEGINNER: Unit tests are like checklists.
// You write code that tests YOUR code.
// Run tests with: dotnet test
// Every [Fact] method is one test case.
// Every [Theory] + [InlineData] runs multiple cases.
//
// GOOD TEST NAME FORMAT: MethodName_Scenario_ExpectedResult
// Example: Encrypt_ValidClaim_ReturnsCipherText
// ============================================================
using ClaimIntake.Domain.Models;
using ClaimIntake.Domain.Services;
using ClaimIntake.Domain.Validation;
using FluentAssertions;
using Xunit;
// Makes assertions read like English: .Should().Be()

namespace ClaimIntake.Tests;

// ── ENCRYPTION TESTS ─────────────────────────────────────────────────────────
public class EncryptionServiceTests
{
    // A valid 256-bit key for testing (generated with KeyHelper.GenerateKey())
    private const string TestKey = "K7mP2xR9vL3nQ6sT8wE1yU4iO0aD5fHjBcNpXqZeWsYd+F=";

    // This helper creates a fresh service for each test
    private static AesEncryptionService CreateService() =>
        new(TestKey);

    // ── TEST 1: Basic round-trip ──────────────────────────────────────────────
    // Encrypt then decrypt should give back the original claim
    [Fact]
    public void Encrypt_ThenDecrypt_ShouldReturnOriginalClaim()
    {
        // ARRANGE: Set up what we need
        var service = CreateService();
        var original = new ClaimDto
        {
            ClaimId = "TEST-001",
            MemberId = "MBR-12345",
            ProviderId = "PRV-99887",
            DiagnosisCode = "A01.1",
            ClaimAmount = 1250.00m,
            SubmittedBy = "testuser"
        };

        // ACT: Do the thing we're testing
        var encrypted = service.Encrypt(original);
        var decrypted = service.Decrypt(encrypted);

        // ASSERT: Check the result is what we expected
        // .Should().Be() is from FluentAssertions — reads like English!
        decrypted.ClaimId.Should().Be(original.ClaimId);
        decrypted.MemberId.Should().Be(original.MemberId);
        decrypted.ProviderId.Should().Be(original.ProviderId);
        decrypted.DiagnosisCode.Should().Be(original.DiagnosisCode);
        decrypted.ClaimAmount.Should().Be(original.ClaimAmount);
        decrypted.SubmittedBy.Should().Be(original.SubmittedBy);
    }

    // ── TEST 2: IV must be unique for each encryption ─────────────────────────
    // Same plaintext encrypted twice should produce DIFFERENT ciphertext
    // This is a critical security property!
    [Fact]
    public void Encrypt_SameClaim_ShouldProduceDifferentCiphertextEachTime()
    {
        var service = CreateService();
        var claim = new ClaimDto
        {
            MemberId = "MBR-001",
            ProviderId = "PRV-001",
            DiagnosisCode = "B99.9",
            ClaimAmount = 100m
        };

        var first = service.Encrypt(claim);
        var second = service.Encrypt(claim);

        // The ciphertext should be DIFFERENT each time (because IV is random)
        first.CipherText.Should().NotBe(second.CipherText,
            because: "each encryption uses a fresh random IV");

        // The IV should be DIFFERENT each time
        first.IV.Should().NotBe(second.IV,
            because: "IV must be unique per message for security");

        // But decrypting either should give back the same original claim
        var decrypted1 = service.Decrypt(first);
        var decrypted2 = service.Decrypt(second);
        decrypted1.MemberId.Should().Be(decrypted2.MemberId);
    }

    // ── TEST 3: Wrong key should fail to decrypt ──────────────────────────────
    [Fact]
    public void Decrypt_WithWrongKey_ShouldThrowException()
    {
        var rightKeyService = CreateService();

        // Generate a DIFFERENT key
        var wrongKey = EncryptionKeyHelper.GenerateNewKey();
        var wrongKeyService = new AesEncryptionService(wrongKey);

        var claim = new ClaimDto
        {
            MemberId = "MBR-001",
            ProviderId = "PRV-001",
            DiagnosisCode = "A01.1",
            ClaimAmount = 500m
        };

        // Encrypt with the right key
        var encrypted = rightKeyService.Encrypt(claim);

        // Try to decrypt with the WRONG key — should throw!
        var act = () => wrongKeyService.Decrypt(encrypted);
        act.Should().Throw<Exception>(
            because: "decrypting with wrong key should fail");
    }

    // ── TEST 4: CipherText must not contain the original data ─────────────────
    [Fact]
    public void Encrypt_CipherText_ShouldNotContainOriginalData()
    {
        var service = CreateService();
        var claim = new ClaimDto
        {
            MemberId = "MBR-SECRET-12345",
            ProviderId = "PRV-001",
            DiagnosisCode = "A01.1",
            ClaimAmount = 99999.99m
        };

        var encrypted = service.Encrypt(claim);

        // The raw ciphertext should NOT contain the member ID
        encrypted.CipherText.Should().NotContain("MBR-SECRET-12345",
            because: "encrypted data must be unreadable");
        encrypted.CipherText.Should().NotContain("99999",
            because: "claim amount must not be visible in ciphertext");
    }

    // ── TEST 5: Invalid key size should throw ─────────────────────────────────
    [Fact]
    public void Constructor_WithInvalidKeySize_ShouldThrow()
    {
        // A 16-byte key (128-bit) — we require 256-bit
        var shortKey = Convert.ToBase64String(new byte[16]);

        var act = () => new AesEncryptionService(shortKey);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*256-bit*", because: "only 256-bit keys are accepted");
    }

    // ── TEST 6: Encrypted payload contains all required fields ────────────────
    [Fact]
    public void Encrypt_Result_ShouldHaveAllRequiredFields()
    {
        var service = CreateService();
        var claim = new ClaimDto
        {
            MemberId = "MBR-001",
            ProviderId = "PRV-001",
            DiagnosisCode = "Z99.89",
            ClaimAmount = 750m
        };

        var result = service.Encrypt(claim);

        result.CipherText.Should().NotBeNullOrEmpty("ciphertext is required");
        result.IV.Should().NotBeNullOrEmpty("IV is required for decryption");
        result.KeyId.Should().NotBeNullOrEmpty("KeyId is required for key rotation");
        result.EncryptedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

// ── VALIDATION TESTS ──────────────────────────────────────────────────────────
public class ClaimValidatorTests
{
    // Helper: creates a VALID claim (all fields correct)
    private static ClaimDto ValidClaim() => new()
    {
        MemberId = "MBR-001",
        ProviderId = "PRV-001",
        DiagnosisCode = "A01.1",
        ClaimAmount = 500m,
        SubmittedBy = "testuser"
    };

    // ── TEST: Valid claim passes all rules ────────────────────────────────────
    [Fact]
    public void Validate_ValidClaim_ShouldPass()
    {
        var (isValid, errors) = ClaimValidator.Validate(ValidClaim());

        isValid.Should().BeTrue("all fields are valid");
        errors.Should().BeEmpty("no errors expected");
    }

    // ── TEST: ICD-10 code validation ──────────────────────────────────────────
    // [Theory] + [InlineData] runs this test multiple times with different values
    [Theory]
    [InlineData("A01.1", true)]   // Standard code with decimal
    [InlineData("Z99.89", true)]   // 2-digit suffix
    [InlineData("B99", true)]   // No decimal (valid)
    [InlineData("J06.9", true)]   // Common cold code
    [InlineData("a01.1", true)]   // Lowercase (validator auto-handles)
    [InlineData("", false)]  // Empty — invalid
    [InlineData("123", false)]  // No letter prefix — invalid
    [InlineData("AB", false)]  // Only 1 digit — invalid
    [InlineData("ABCDEF", false)]  // No digits — invalid
    [InlineData("1AB.1", false)]  // Digit first — invalid
    [InlineData("A1.1", false)]  // Only 1 digit — invalid
    public void Validate_DiagnosisCode_ShouldMatchExpected(
        string code, bool expectedValid)
    {
        var claim = ValidClaim();
        claim.DiagnosisCode = code;

        var (isValid, _) = ClaimValidator.Validate(claim);

        isValid.Should().Be(expectedValid,
            because: $"'{code}' should be {(expectedValid ? "valid" : "invalid")}");
    }

    // ── TEST: Claim amount boundaries ─────────────────────────────────────────
    [Theory]
    [InlineData(0.01, true)]   // Minimum valid amount
    [InlineData(100.00, true)]   // Normal amount
    [InlineData(999999.99, true)]   // Maximum valid amount
    [InlineData(0.00, false)]  // Zero — invalid
    [InlineData(-1.00, false)]  // Negative — invalid
    [InlineData(1000000.00, false)]  // Over maximum — invalid
    public void Validate_ClaimAmount_ShouldEnforceRange(
        decimal amount, bool expectedValid)
    {
        var claim = ValidClaim();
        claim.ClaimAmount = amount;

        var (isValid, _) = ClaimValidator.Validate(claim);

        isValid.Should().Be(expectedValid,
            because: $"{amount:C} should be {(expectedValid ? "valid" : "invalid")}");
    }

    // ── TEST: Required fields ─────────────────────────────────────────────────
    [Fact]
    public void Validate_EmptyMemberId_ShouldFail()
    {
        var claim = ValidClaim();
        claim.MemberId = "";

        var (isValid, errors) = ClaimValidator.Validate(claim);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Member ID"),
            because: "error message should mention 'Member ID'");
    }

    [Fact]
    public void Validate_EmptyProviderId_ShouldFail()
    {
        var claim = ValidClaim();
        claim.ProviderId = "  ";  // whitespace only

        var (isValid, errors) = ClaimValidator.Validate(claim);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Provider ID"));
    }

    // ── TEST: Multiple errors reported at once ────────────────────────────────
    [Fact]
    public void Validate_MultipleErrors_ShouldReportAll()
    {
        var claim = new ClaimDto
        {
            MemberId = "",        // Error 1
            ProviderId = "",        // Error 2
            DiagnosisCode = "INVALID", // Error 3
            ClaimAmount = -100m,     // Error 4
            SubmittedBy = ""         // Error 5
        };

        var (isValid, errors) = ClaimValidator.Validate(claim);

        isValid.Should().BeFalse();
        errors.Should().HaveCountGreaterThan(1,
            because: "multiple fields are invalid");
    }
}

// ── CLAIM DTO TESTS ───────────────────────────────────────────────────────────
public class ClaimDtoTests
{
    [Fact]
    public void NewClaimDto_ShouldHaveAutoGeneratedClaimId()
    {
        var claim = new ClaimDto();

        claim.ClaimId.Should().NotBeNullOrEmpty("ClaimId is auto-generated");
        // Verify it looks like a GUID
        Guid.TryParse(claim.ClaimId, out _).Should().BeTrue(
            because: "ClaimId should be a valid GUID");
    }

    [Fact]
    public void TwoNewClaimDtos_ShouldHaveDifferentClaimIds()
    {
        var claim1 = new ClaimDto();
        var claim2 = new ClaimDto();

        claim1.ClaimId.Should().NotBe(claim2.ClaimId,
            because: "each claim gets a unique ID");
    }

    [Fact]
    public void NewClaimDto_SubmittedAt_ShouldBeCloseToNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var claim = new ClaimDto();
        var after = DateTime.UtcNow.AddSeconds(1);

        claim.SubmittedAt.Should().BeAfter(before)
            .And.BeBefore(after);
    }
}
