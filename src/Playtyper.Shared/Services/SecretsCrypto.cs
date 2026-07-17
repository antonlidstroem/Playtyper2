using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Playtyper.Shared.Services;

/// <summary>
/// Kryptering av GitHub Actions-secrets (t.ex. PLAYTYPUS_TOKEN,
/// CLOUDFLARE_API_TOKEN) för GitHubRepoService.SetSecretsAsync.
///
/// VARFÖR DEN HÄR FILEN FINNS: PackWizard (CLI-originalet) använder NuGet-
/// paketet Sodium.Core, som är ett P/Invoke-wrapper runt native libsodium.
/// Det fungerar utmärkt i en konsolapp men INTE i Blazor WebAssembly — WASM
/// kan inte P/Invoke:a in i en native .so/.dll, så samma paket skulle krascha
/// runtime i webbläsaren (och vara en två-kodvägar-risk om vi bara bytte det
/// på MAUI-sidan). Byggmiljön som skapade den här filen hade dessutom ingen
/// nätverksåtkomst till nuget.org för att ens kontrollera om ett alternativt
/// paket (BouncyCastle m.fl.) finns i en WASM-kompatibel version just nu.
///
/// Lösningen: GitHubs "Secrets"-API kräver specifikt NaCl/libsodium's
/// crypto_box_seal (X25519 + BLAKE2b + XSalsa20-Poly1305) — inte ett valfritt
/// krypto-schema, eftersom GitHubs backend kör libsodiums crypto_box_seal_open
/// för att låsa upp värdet. Den här filen implementerar därför exakt den
/// algoritmen från grunden, i ren hanterad C# (BigInteger + vanlig
/// heltalsaritmetik) utan några tredjepartsberoenden alls — fungerar
/// identiskt på WASM, Android, Windows och skrivbord.
///
/// VIKTIGT — LÄS INNAN DU LITAR PÅ DEN HÄR FILEN FÖR RIKTIGA SECRETS:
/// Varje primitiv nedan (X25519 enligt RFC 7748, BLAKE2b enligt RFC 7693,
/// Salsa20/HSalsa20/XSalsa20 enligt DJB:s specifikationer, Poly1305 enligt
/// RFC 8439) skrevs mot de publicerade specifikationerna och verifierades
/// därefter BYTE FÖR BYTE mot en riktig libsodium-installation (via Pythons
/// PyNaCl, som binder mot äkta libsodium) i byggmiljön — inklusive en helt
/// fristående pipeline-test (egen X25519 + egen BLAKE2b + egen Salsa20/
/// Poly1305, INGA orakel-anrop i själva beräkningen) där riktig libsodium
/// sedan bekräftade att den kunde låsa upp resultatet. Se SelfTest() nedan,
/// som bäddar in exakt den testvektorn. DEN HÄR C#-TRANSKRIPTIONEN HAR DÄREMOT
/// ALDRIG KÖRTS — sandlådan som byggde detta saknade .NET SDK/NuGet-åtkomst
/// för att kompilera något alls (se README.md i solution-roten). Kör
/// SecretsCrypto.SelfTest() den allra första gången du bygger projektet,
/// innan du sätter ett riktigt token, för att bekräfta att just din .NET-
/// runtime producerar identiskt resultat.
///
/// Fältaritmetiken använder System.Numerics.BigInteger istället för
/// fast-bredd limb-aritmetik (så som produktions-libsodium gör för
/// prestanda) — enklare att skriva och läsa korrekt, på bekostnad av att INTE
/// vara constant-time. Ett medvetet, rimligt val: det här är en engångs-
/// kryptering av 1–3 korta secrets inför ett enda HTTPS-anrop, inte en
/// höghastighetstjänst där tidsattacker från en samlokaliserad angripare är
/// ett realistiskt hot.
/// </summary>
public static class SecretsCrypto
{
    // ════════════════════════════════════════════════════════════════════
    // X25519 — RFC 7748 Montgomery-stege
    // ════════════════════════════════════════════════════════════════════

    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private const int A24 = 121665;

    private static BigInteger Mod(BigInteger x)
    {
        var r = x % P;
        return r.Sign < 0 ? r + P : r;
    }

    private static BigInteger DecodeLittleEndianUnsigned(ReadOnlySpan<byte> bytes) =>
        new(bytes, isUnsigned: true, isBigEndian: false);

    private static byte[] EncodeLittleEndian32(BigInteger value)
    {
        value = Mod(value);
        var result = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            result[i] = (byte)(value & 0xff);
            value >>= 8;
        }
        return result;
    }

    /// <summary>Rå X25519-skalärmultiplikation: X25519(scalar, uCoordinate).</summary>
    public static byte[] X25519(byte[] scalar32, byte[] uCoordinate32)
    {
        var k = (byte[])scalar32.Clone();
        k[0]  &= 248;
        k[31] &= 127;
        k[31] |= 64;
        var kInt = DecodeLittleEndianUnsigned(k);

        var uClamped = (byte[])uCoordinate32.Clone();
        uClamped[31] &= 127;
        var u = DecodeLittleEndianUnsigned(uClamped);

        BigInteger x1 = u, x2 = 1, z2 = 0, x3 = u, z3 = 1;
        var swap = 0;

        for (var t = 254; t >= 0; t--)
        {
            var kt = (int)((kInt >> t) & 1);
            swap ^= kt;
            if (swap == 1)
            {
                (x2, x3) = (x3, x2);
                (z2, z3) = (z3, z2);
            }
            swap = kt;

            var a  = Mod(x2 + z2);
            var aa = Mod(a * a);
            var b  = Mod(x2 - z2);
            var bb = Mod(b * b);
            var e  = Mod(aa - bb);
            var c  = Mod(x3 + z3);
            var d  = Mod(x3 - z3);
            var da = Mod(d * a);
            var cb = Mod(c * b);

            x3 = Mod((da + cb) * (da + cb));
            z3 = Mod(x1 * Mod((da - cb) * (da - cb)));
            x2 = Mod(aa * bb);
            z2 = Mod(e * Mod(aa + A24 * e));
        }

        if (swap == 1)
        {
            (x2, x3) = (x3, x2);
            (z2, z3) = (z3, z2);
        }

        var zInv = BigInteger.ModPow(z2, P - 2, P);
        return EncodeLittleEndian32(Mod(x2 * zInv));
    }

    private static readonly byte[] BasePoint9 = BuildBasePoint();
    private static byte[] BuildBasePoint()
    {
        var b = new byte[32];
        b[0] = 9;
        return b;
    }

    public static byte[] X25519BasePoint(byte[] scalar32) => X25519(scalar32, BasePoint9);

    // ════════════════════════════════════════════════════════════════════
    // BLAKE2b — RFC 7693 (obetygad, ingen personalisering — det är vad
    // libsodiums crypto_generichash faktiskt är, verifierat mot PyNaCl)
    // ════════════════════════════════════════════════════════════════════

    private static readonly ulong[] Blake2bIv =
    [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179
    ];

    private static readonly int[][] Blake2bSigma =
    [
        [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15],
        [14,10, 4, 8, 9,15,13, 6, 1,12, 0, 2,11, 7, 5, 3],
        [11, 8,12, 0, 5, 2,15,13,10,14, 3, 6, 7, 1, 9, 4],
        [ 7, 9, 3, 1,13,12,11,14, 2, 6, 5,10, 4, 0,15, 8],
        [ 9, 0, 5, 7, 2, 4,10,15,14, 1,11,12, 6, 8, 3,13],
        [ 2,12, 6,10, 0,11, 8, 3, 4,13, 7, 5,15,14, 1, 9],
        [12, 5, 1,15,14,13, 4,10, 0, 7, 6, 3, 9, 2, 8,11],
        [13,11, 7,14,12, 1, 3, 9, 5, 0,15, 4, 8, 6, 2,10],
        [ 6,15,14, 9,11, 3, 0, 8,12, 2,13, 7, 1, 4,10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5,15,11, 9,14, 3,12,13, 0],
        [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15],
        [14,10, 4, 8, 9,15,13, 6, 1,12, 0, 2,11, 7, 5, 3],
    ];

    private static ulong Rotr64(ulong x, int n) => (x >> n) | (x << (64 - n));

    private static void BlakeG(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
    {
        unchecked
        {
            v[a] = v[a] + v[b] + x;
            v[d] = Rotr64(v[d] ^ v[a], 32);
            v[c] = v[c] + v[d];
            v[b] = Rotr64(v[b] ^ v[c], 24);
            v[a] = v[a] + v[b] + y;
            v[d] = Rotr64(v[d] ^ v[a], 16);
            v[c] = v[c] + v[d];
            v[b] = Rotr64(v[b] ^ v[c], 63);
        }
    }

    private static ulong ReadLE64(byte[] b, int offset)
    {
        ulong r = 0;
        for (var i = 7; i >= 0; i--) r = (r << 8) | b[offset + i];
        return r;
    }

    private static void WriteLE64(ulong value, byte[] output, int offset)
    {
        for (var i = 0; i < 8; i++)
        {
            output[offset + i] = (byte)(value & 0xff);
            value >>= 8;
        }
    }

    private static void BlakeCompress(ulong[] h, byte[] block128, BigInteger t, bool final)
    {
        var m = new ulong[16];
        for (var i = 0; i < 16; i++) m[i] = ReadLE64(block128, i * 8);

        var v = new ulong[16];
        Array.Copy(h, v, 8);
        Array.Copy(Blake2bIv, 0, v, 8, 8);

        var tLow  = (ulong)(t & ulong.MaxValue);
        var tHigh = (ulong)(t >> 64);
        unchecked
        {
            v[12] ^= tLow;
            v[13] ^= tHigh;
            if (final) v[14] = ~v[14];
        }

        for (var round = 0; round < 12; round++)
        {
            var s = Blake2bSigma[round];
            BlakeG(v, 0, 4,  8, 12, m[s[0]],  m[s[1]]);
            BlakeG(v, 1, 5,  9, 13, m[s[2]],  m[s[3]]);
            BlakeG(v, 2, 6, 10, 14, m[s[4]],  m[s[5]]);
            BlakeG(v, 3, 7, 11, 15, m[s[6]],  m[s[7]]);
            BlakeG(v, 0, 5, 10, 15, m[s[8]],  m[s[9]]);
            BlakeG(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            BlakeG(v, 2, 7,  8, 13, m[s[12]], m[s[13]]);
            BlakeG(v, 3, 4,  9, 14, m[s[14]], m[s[15]]);
        }

        unchecked
        {
            for (var i = 0; i < 8; i++) h[i] = h[i] ^ v[i] ^ v[i + 8];
        }
    }

    /// <summary>BLAKE2b (obetygad) med valfri utdatalängd — motsvarar libsodiums crypto_generichash.</summary>
    public static byte[] Blake2b(byte[] data, int digestSize)
    {
        var h = (ulong[])Blake2bIv.Clone();
        unchecked { h[0] ^= 0x01010000UL ^ (ulong)digestSize; }

        var n = data.Length;
        BigInteger t = 0;

        if (n == 0)
        {
            BlakeCompress(h, new byte[128], 0, true);
        }
        else
        {
            var i = 0;
            while (i < n)
            {
                var remaining = n - i;
                var isLast = remaining <= 128;
                var block = new byte[128];
                var chunk = Math.Min(128, remaining);
                Array.Copy(data, i, block, 0, chunk);

                if (isLast)
                {
                    t += remaining;
                    BlakeCompress(h, block, t, true);
                }
                else
                {
                    t += 128;
                    BlakeCompress(h, block, t, false);
                }
                i += 128;
            }
        }

        var full = new byte[64];
        for (var i = 0; i < 8; i++) WriteLE64(h[i], full, i * 8);
        return full[..digestSize];
    }

    // ════════════════════════════════════════════════════════════════════
    // Salsa20 / HSalsa20 / XSalsa20 — DJB:s specifikationer
    // ════════════════════════════════════════════════════════════════════

    private static readonly byte[] Sigma = Encoding.ASCII.GetBytes("expand 32-byte k");

    private static uint ReadLE32(byte[] b, int offset) =>
        (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));

    private static void WriteLE32(uint value, byte[] output, int offset)
    {
        output[offset]     = (byte)(value & 0xff);
        output[offset + 1] = (byte)((value >> 8) & 0xff);
        output[offset + 2] = (byte)((value >> 16) & 0xff);
        output[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static uint[] ToWords(byte[] data)
    {
        var words = new uint[data.Length / 4];
        for (var i = 0; i < words.Length; i++) words[i] = ReadLE32(data, i * 4);
        return words;
    }

    private static uint Rotl32(uint x, int n) => (x << n) | (x >> (32 - n));

    private static void SalsaDoubleRound(uint[] x)
    {
        unchecked
        {
            x[4]  ^= Rotl32(x[0] + x[12], 7);  x[8]  ^= Rotl32(x[4] + x[0], 9);
            x[12] ^= Rotl32(x[8] + x[4], 13);  x[0]  ^= Rotl32(x[12] + x[8], 18);
            x[9]  ^= Rotl32(x[5] + x[1], 7);   x[13] ^= Rotl32(x[9] + x[5], 9);
            x[1]  ^= Rotl32(x[13] + x[9], 13); x[5]  ^= Rotl32(x[1] + x[13], 18);
            x[14] ^= Rotl32(x[10] + x[6], 7);  x[2]  ^= Rotl32(x[14] + x[10], 9);
            x[6]  ^= Rotl32(x[2] + x[14], 13); x[10] ^= Rotl32(x[6] + x[2], 18);
            x[3]  ^= Rotl32(x[15] + x[11], 7); x[7]  ^= Rotl32(x[3] + x[15], 9);
            x[11] ^= Rotl32(x[7] + x[3], 13);  x[15] ^= Rotl32(x[11] + x[7], 18);

            x[1]  ^= Rotl32(x[0] + x[3], 7);   x[2]  ^= Rotl32(x[1] + x[0], 9);
            x[3]  ^= Rotl32(x[2] + x[1], 13);  x[0]  ^= Rotl32(x[3] + x[2], 18);
            x[6]  ^= Rotl32(x[5] + x[4], 7);   x[7]  ^= Rotl32(x[6] + x[5], 9);
            x[4]  ^= Rotl32(x[7] + x[6], 13);  x[5]  ^= Rotl32(x[4] + x[7], 18);
            x[11] ^= Rotl32(x[10] + x[9], 7);  x[8]  ^= Rotl32(x[11] + x[10], 9);
            x[9]  ^= Rotl32(x[8] + x[11], 13); x[10] ^= Rotl32(x[9] + x[8], 18);
            x[12] ^= Rotl32(x[15] + x[14], 7); x[13] ^= Rotl32(x[12] + x[15], 9);
            x[14] ^= Rotl32(x[13] + x[12], 13);x[15] ^= Rotl32(x[14] + x[13], 18);
        }
    }

    private static uint[] BuildState(uint[] key8, uint n0, uint n1, uint n2, uint n3)
    {
        var s = ToWords(Sigma);
        return
        [
            s[0], key8[0], key8[1], key8[2],
            key8[3], s[1], n0, n1,
            n2, n3, s[2], key8[4],
            key8[5], key8[6], key8[7], s[3]
        ];
    }

    private static byte[] HSalsa20(byte[] key32, byte[] nonce16)
    {
        var n = ToWords(nonce16);
        var x = BuildState(ToWords(key32), n[0], n[1], n[2], n[3]);
        for (var i = 0; i < 10; i++) SalsaDoubleRound(x);

        var outWords = new[] { x[0], x[5], x[10], x[15], x[6], x[7], x[8], x[9] };
        var result = new byte[32];
        for (var i = 0; i < 8; i++) WriteLE32(outWords[i], result, i * 4);
        return result;
    }

    private static byte[] SalsaBlock(uint[] key8, uint n0, uint n1, uint c0, uint c1)
    {
        var x = BuildState(key8, n0, n1, c0, c1);
        var orig = (uint[])x.Clone();
        for (var i = 0; i < 10; i++) SalsaDoubleRound(x);

        var result = new byte[64];
        unchecked
        {
            for (var i = 0; i < 16; i++) WriteLE32(x[i] + orig[i], result, i * 4);
        }
        return result;
    }

    private static byte[] XSalsa20Keystream(byte[] key32, byte[] nonce24, int nBytes)
    {
        var nonce16 = nonce24[..16];
        var nonce8  = nonce24[16..24];
        var subKey8 = ToWords(HSalsa20(key32, nonce16));
        var n2 = ToWords(nonce8);

        var output = new byte[nBytes];
        var written = 0;
        ulong counter = 0;
        while (written < nBytes)
        {
            var block = SalsaBlock(subKey8, n2[0], n2[1], (uint)(counter & 0xffffffff), (uint)(counter >> 32));
            var take = Math.Min(64, nBytes - written);
            Array.Copy(block, 0, output, written, take);
            written += take;
            counter++;
        }
        return output;
    }

    // ════════════════════════════════════════════════════════════════════
    // Poly1305 — RFC 8439
    // ════════════════════════════════════════════════════════════════════

    private static readonly BigInteger Poly1305P = BigInteger.Pow(2, 130) - 5;

    private static byte[] Poly1305Mac(byte[] msg, byte[] key32)
    {
        var r = (byte[])key32[..16].Clone();
        r[3]  &= 15;  r[7]  &= 15;  r[11] &= 15;  r[15] &= 15;
        r[4]  &= 252; r[8]  &= 252; r[12] &= 252;

        var rInt = DecodeLittleEndianUnsigned(r);
        var s    = DecodeLittleEndianUnsigned(key32[16..32]);

        BigInteger acc = 0;
        for (var i = 0; i < msg.Length; i += 16)
        {
            var chunk = msg.AsSpan(i, Math.Min(16, msg.Length - i));
            var padded = new byte[chunk.Length + 1];
            chunk.CopyTo(padded);
            padded[^1] = 1; // motsvarar Pythonens "+ (1 << (8*len(block)))" - en extra hög etta
            var n = DecodeLittleEndianUnsigned(padded);
            acc = (acc + n) * rInt % Poly1305P;
        }

        var tag = (acc + s) % BigInteger.Pow(2, 128);
        var tagBytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            tagBytes[i] = (byte)(tag & 0xff);
            tag >>= 8;
        }
        return tagBytes;
    }

    private static byte[] CryptoSecretBox(byte[] msg, byte[] nonce24, byte[] key32)
    {
        var padded = new byte[32 + msg.Length];
        Array.Copy(msg, 0, padded, 32, msg.Length);

        var keystream = XSalsa20Keystream(key32, nonce24, padded.Length);
        var cPadded = new byte[padded.Length];
        for (var i = 0; i < padded.Length; i++) cPadded[i] = (byte)(padded[i] ^ keystream[i]);

        var polyKey    = cPadded[..32];
        var ciphertext = cPadded[32..];
        var mac        = Poly1305Mac(ciphertext, polyKey);

        var result = new byte[16 + ciphertext.Length];
        Array.Copy(mac, 0, result, 0, 16);
        Array.Copy(ciphertext, 0, result, 16, ciphertext.Length);
        return result;
    }

    // ════════════════════════════════════════════════════════════════════
    // crypto_box_seal — det GitHubs Secrets-API faktiskt förväntar sig
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Krypterar <paramref name="plaintext"/> för mottagaren med publik
    /// nyckel <paramref name="recipientPublicKey32"/>, i exakt det format
    /// libsodiums crypto_box_seal (och därmed GitHubs Secrets-API) förväntar
    /// sig: en slumpad 32-byte ephemeral publik nyckel följt av en
    /// XSalsa20-Poly1305-krypterad payload — totalt 48 byte overhead.
    /// </summary>
    public static byte[] CryptoBoxSeal(byte[] plaintext, byte[] recipientPublicKey32)
    {
        var ephemeralSecretKey = RandomNumberGenerator.GetBytes(32);
        var ephemeralPublicKey = X25519BasePoint(ephemeralSecretKey);

        var nonceInput = new byte[64];
        Array.Copy(ephemeralPublicKey, 0, nonceInput, 0, 32);
        Array.Copy(recipientPublicKey32, 0, nonceInput, 32, 32);
        var nonce = Blake2b(nonceInput, 24);

        var sharedSecret = X25519(ephemeralSecretKey, recipientPublicKey32);
        var beforenmKey  = HSalsa20(sharedSecret, new byte[16]);

        var ciphertext = CryptoSecretBox(plaintext, nonce, beforenmKey);

        var sealedBox = new byte[32 + ciphertext.Length];
        Array.Copy(ephemeralPublicKey, 0, sealedBox, 0, 32);
        Array.Copy(ciphertext, 0, sealedBox, 32, ciphertext.Length);
        return sealedBox;
    }

    /// <summary>
    /// Bekvämlighetsmetod för GitHubRepoService: krypterar en secret-sträng
    /// (UTF8) mot repots base64-kodade publika nyckel, returnerar base64 —
    /// det format Secrets-API:et (PUT /repos/{o}/{r}/actions/secrets/{name})
    /// förväntar sig i fältet "encrypted_value".
    /// </summary>
    public static string EncryptSecret(string base64PublicKey, string secretValue)
    {
        var publicKey   = Convert.FromBase64String(base64PublicKey);
        var secretBytes = Encoding.UTF8.GetBytes(secretValue);
        var sealedBox   = CryptoBoxSeal(secretBytes, publicKey);
        return Convert.ToBase64String(sealedBox);
    }

    // ════════════════════════════════════════════════════════════════════
    // Självtest — kör den här FÖRST efter att du fått projektet att bygga,
    // innan du litar på filen för ett riktigt token. Testvektorn genererades
    // och verifierades mot en riktig libsodium-installation (se filens
    // klassdoc-kommentar ovan för hur).
    // ════════════════════════════════════════════════════════════════════

    private static byte[] HexToBytes(string hex)
    {
        var result = new byte[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }

    /// <summary>
    /// Kör alla primitiver mot en fast, förberäknad testvektor och returnerar
    /// true om resultatet stämmer exakt. Kastar inget — returnerar bara
    /// false vid avvikelse, så anroparen kan visa ett tydligt varningsmeddelande
    /// istället för att krascha appen.
    /// </summary>
    public static bool SelfTest()
    {
        try
        {
            var recipientSk = new byte[32];
            for (var i = 0; i < 32; i++) recipientSk[i] = (byte)i;

            var ephemeralSk = new byte[32];
            for (var i = 0; i < 32; i++) ephemeralSk[i] = (byte)((i * 7 + 3) % 256);

            var recipientPk = X25519BasePoint(recipientSk);
            var msg = Encoding.UTF8.GetBytes("PLAYTYPUS_TOKEN=ghp_test_vector_value");

            // Samma steg som CryptoBoxSeal, men med FAST ephemeral-nyckel
            // istället för slumpad, så resultatet blir reproducerbart.
            var ephemeralPk = X25519BasePoint(ephemeralSk);
            var nonceInput = new byte[64];
            Array.Copy(ephemeralPk, 0, nonceInput, 0, 32);
            Array.Copy(recipientPk, 0, nonceInput, 32, 32);
            var nonce = Blake2b(nonceInput, 24);
            var shared = X25519(ephemeralSk, recipientPk);
            var beforenm = HSalsa20(shared, new byte[16]);
            var ciphertext = CryptoSecretBox(msg, nonce, beforenm);

            var sealedBox = new byte[32 + ciphertext.Length];
            Array.Copy(ephemeralPk, 0, sealedBox, 0, 32);
            Array.Copy(ciphertext, 0, sealedBox, 32, ciphertext.Length);

            const string expectedHex =
                "bb50ff9e82a574cfbf820e97f60fb9c143ec7415cf514f8cfd98eff59e05961" +
                "467544993fe037b9e6bbd004fbe8c910011961ad0c0b76553cf57333358b09d" +
                "9a83cde1d53708fb91c54678bb3814ee959380f21699";
            var expected = HexToBytes(expectedHex);

            return sealedBox.AsSpan().SequenceEqual(expected);
        }
        catch
        {
            return false;
        }
    }
}
