package parity

import app.keemobile.kotpass.cryptography.EncryptedValue
import app.keemobile.kotpass.cryptography.format.BaseCiphers
import app.keemobile.kotpass.database.Credentials
import app.keemobile.kotpass.database.KeePassDatabase
import app.keemobile.kotpass.database.decode
import app.keemobile.kotpass.database.encode
import app.keemobile.kotpass.database.modifiers.binaries
import app.keemobile.kotpass.database.header.DatabaseHeader
import app.keemobile.kotpass.database.header.KdfParameters
import app.keemobile.kotpass.models.AutoTypeData
import app.keemobile.kotpass.models.AutoTypeItem
import app.keemobile.kotpass.models.BinaryData
import app.keemobile.kotpass.models.BinaryReference
import app.keemobile.kotpass.models.Entry
import app.keemobile.kotpass.models.EntryFields
import app.keemobile.kotpass.models.EntryValue
import app.keemobile.kotpass.models.Group
import app.keemobile.kotpass.models.Meta
import app.keemobile.kotpass.models.TimeData
import java.io.FileInputStream
import java.io.FileOutputStream
import java.security.MessageDigest
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.UUID

private const val FIXTURE_PASSWORD = "kdbx-parity-fixture-not-a-secret"

private val cipherProviders = BaseCiphers.entries

fun main(args: Array<String>) {
    when (args.getOrNull(0)) {
        "create" -> create(java.io.File(args[1]))
        "dump" -> dump(java.io.File(args[1]))
        "probe-bin" -> probeBin(java.io.File(args[1]))
        "verify-bin" -> verifyBin(java.io.File(args[1]))
        else -> error("usage: create <file> | dump <file> | probe-bin <file> | verify-bin <file>")
    }
}

private fun create(output: java.io.File) {
    val credentials = Credentials.from(EncryptedValue.fromString(FIXTURE_PASSWORD))
    val meta = Meta(
        generator = "Monica Password Manager",
        name = "Parity Fixture"
    )
    val base = KeePassDatabase.Ver4x.create(
        rootName = "Root",
        meta = meta,
        credentials = credentials
    )
    val salt = (base.header.kdfParameters as KdfParameters.Argon2).salt
    val database = base.copy(
        header = base.header.copy(
            cipherId = BaseCiphers.Aes.uuid,
            kdfParameters = KdfParameters.Argon2(
                variant = KdfParameters.Argon2.Variant.Argon2d,
                salt = salt,
                parallelism = 2U,
                memory = 32UL * 1024UL * 1024UL,
                iterations = 8U,
                version = 0x13U,
                secretKey = null,
                associatedData = null
            )
        )
    )

    val attachment = BinaryData.Uncompressed(
        memoryProtection = false,
        rawContent = ByteArray(32) { (it % 251).toByte() }
    )
    val rootEntry = entry(
        title = "parity-root-01",
        username = "parity-user-a",
        custom = listOf("MonicaLocalId" to EntryValue.Plain("1001"))
    )
    val oldRevision = rootEntry.copy(
        fields = fields("parity-root-01", "parity-user-a", "ticket-old", listOf()),
        times = fixedTimes()
    )
    val nestedEntry = entry(
        title = "parity-work-02",
        username = "parity-user-b",
        custom = listOf(
            "MonicaLocalId" to EntryValue.Plain("1002"),
            "Card Number" to EntryValue.Encrypted(EncryptedValue.fromString("4111111111111111"))
        ),
        binaries = listOf(BinaryReference(attachment.hash, "parity.bin")),
        autoType = AutoTypeData(
            enabled = true,
            items = listOf(AutoTypeItem("chrome.exe", "{USERNAME}{TAB}{PASSWORD}{ENTER}"))
        ),
        history = listOf(oldRevision)
    )

    val root = database.content.group.copy(
        entries = listOf(rootEntry),
        groups = listOf(
            Group(
                uuid = UUID.fromString("22222222-3333-4444-5555-666666666666"),
                name = "Work",
                times = TimeData.create(),
                entries = listOf(nestedEntry)
            )
        )
    )
    val withContent = database.copy(content = database.content.copy(group = root))
    val populated = (withContent as KeePassDatabase.Ver4x).copy(
        innerHeader = withContent.innerHeader.copy(
            binaries = linkedMapOf(attachment.hash to attachment)
        )
    )

    FileOutputStream(output).use { populated.encode(it, cipherProviders = cipherProviders) }
    println("created bytes=${output.length()} entries=2 groups=1")
}

private fun fixedTimes() = TimeData(
    creationTime = Instant.parse("2026-01-02T03:04:05Z"),
    lastAccessTime = Instant.parse("2026-01-02T03:04:05Z"),
    lastModificationTime = Instant.parse("2026-01-02T03:04:05Z"),
    locationChanged = Instant.parse("2026-01-02T03:04:05Z"),
    expiryTime = null,
    expires = false,
    usageCount = 0
)

private fun entry(
    title: String,
    username: String,
    custom: List<Pair<String, EntryValue>>,
    binaries: List<BinaryReference> = listOf(),
    autoType: AutoTypeData? = null,
    history: List<Entry> = listOf()
) = Entry(
    uuid = UUID.randomUUID(),
    times = TimeData.create(),
    autoType = autoType,
    fields = fields(title, username, "ticket-1001", custom),
    binaries = binaries,
    history = history
)

private fun fields(
    title: String,
    username: String,
    reference: String,
    custom: List<Pair<String, EntryValue>>
) = EntryFields.of(
    *(
        listOf(
            "Title" to EntryValue.Plain(title),
            "UserName" to EntryValue.Plain(username),
            "Password" to EntryValue.Encrypted(EncryptedValue.fromString("parity-secret-$username")),
            "URL" to EntryValue.Plain("https://$username.example.test"),
            "Notes" to EntryValue.Plain("parity notes"),
            "Reference" to EntryValue.Plain(reference)
        ) + custom
        ).toTypedArray()
)

private fun dump(file: java.io.File) {
    val credentials = Credentials.from(EncryptedValue.fromString(FIXTURE_PASSWORD))
    val database = FileInputStream(file).use {
        KeePassDatabase.decode(it, credentials, cipherProviders = cipherProviders)
    }
    val header = database.header
    println("format=KDBX-${header.version.major}.${header.version.minor}")
    println("cipherId=${header.cipherId}")
    println("compression=${header.compression}")
    when (header) {
        is DatabaseHeader.Ver4x -> {
            when (val kdf = header.kdfParameters) {
                is KdfParameters.Aes -> println("kdf=aes rounds=${kdf.rounds} seedBytes=${kdf.seed.size}")
                is KdfParameters.Argon2 -> println(
                    "kdf=argon2 variant=${kdf.variant} parallelism=${kdf.parallelism} " +
                        "memoryBytes=${kdf.memory} iterations=${kdf.iterations} " +
                        "version=0x${kdf.version.toString(16)} saltBytes=${kdf.salt.size}"
                )
            }
            println("publicCustomData=${header.publicCustomData.keys.sorted()}")
            val inner = (database as KeePassDatabase.Ver4x).innerHeader
            println("innerStream=${inner.randomStreamId} keyBytes=${inner.randomStreamKey.size}")
            println("innerBinaries=${database.binaries.size}")
        }
        is DatabaseHeader.Ver3x -> println(
            "kdf=aes rounds=${header.transformRounds} innerStream=${header.innerRandomStreamId} " +
                "streamStartBytes=${header.streamStartBytes.size}"
        )
    }
    val meta = database.content.meta
    println(
        "meta generator=${meta.generator} name=${meta.name} recycleBin=${meta.recycleBinEnabled} " +
            "historyMaxItems=${meta.historyMaxItems} historyMaxSize=${meta.historyMaxSize} " +
            "maintenanceHistoryDays=${meta.maintenanceHistoryDays} " +
            "memoryProtection=${meta.memoryProtection.sortedBy { it.name }} " +
            "customIcons=${meta.customIcons.size} settingsChanged=${iso(meta.settingsChanged)}"
    )
    println("deletedObjects=${database.content.deletedObjects.size}")
    dumpGroup(database.content.group, "Root")
}

private fun dumpGroup(group: Group, path: String) {
    println(
        "group path=$path name=${group.name} uuid=${group.uuid} icon=${group.icon} " +
            "customIcon=${group.customIconUuid} expanded=${group.expanded} " +
            "autoType=${group.enableAutoType} searching=${group.enableSearching} " +
            "defaultAutoTypeSequence=${group.defaultAutoTypeSequence} times=${times(group.times)}"
    )
    group.entries.sortedBy { it.fields.title?.content ?: "" }.forEach {
        dumpEntry(it, "$path/${it.fields.title?.content}")
    }
    group.groups.sortedBy { it.name }.forEach { dumpGroup(it, "$path/${it.name}") }
}

private fun dumpEntry(entry: Entry, path: String) {
    println(
        "entry path=$path uuid=${entry.uuid} icon=${entry.icon} customIcon=${entry.customIconUuid} " +
            "tags=${entry.tags} qualityCheck=${entry.qualityCheck} " +
            "previousParentGroup=${entry.previousParentGroup} " +
            "history=${entry.history.size} times=${times(entry.times)}"
    )
    val autoType = entry.autoType
    if (autoType == null) {
        println("  autoType=none")
    } else {
        println(
            "  autoType enabled=${autoType.enabled} obfuscation=${autoType.obfuscation} " +
                "default=${autoType.defaultSequence} items=${autoType.items.size}"
        )
        autoType.items.forEach {
            println("  autoTypeItem window=${it.window} sequence=${it.keystrokeSequence}")
        }
    }
    entry.fields.keys.sorted().forEach { key ->
        val value = entry.fields[key]!!
        println(
            "  field name=$key protected=${value is EntryValue.Encrypted} " +
                "chars=${value.content.length} sha=${fingerprint(value.content)}"
        )
    }
    entry.binaries.sortedBy { it.name }.forEach {
        println("  binary name=${it.name} hash=${it.hash.hex().take(16)}")
    }
    entry.customData.keys.sorted().forEach { println("  customData key=$it") }
    entry.history.forEach { history ->
        println(
            "  historyItem times=${times(history.times)} " +
                "fields=${history.fields.keys.sorted()} " +
                "binaries=${history.binaries.map { it.name }}"
        )
        history.fields.keys.sorted().forEach { key ->
            println("    historyField name=$key sha=${fingerprint(history.fields[key]!!.content)}")
        }
    }
}

private fun fingerprint(text: String): String =
    MessageDigest.getInstance("SHA-256")
        .digest(text.toByteArray(Charsets.UTF_8))
        .joinToString("") { "%02x".format(it) }
        .take(16)

private fun times(data: TimeData?): String {
    if (data == null) {
        return "none"
    }
    return "created=${iso(data.creationTime)} modified=${iso(data.lastModificationTime)} " +
        "accessed=${iso(data.lastAccessTime)} location=${iso(data.locationChanged)} " +
        "expiry=${iso(data.expiryTime)} expires=${data.expires} usage=${data.usageCount}"
}

private fun iso(instant: Instant?): String =
    instant?.atZone(ZoneOffset.UTC)?.format(DateTimeFormatter.ISO_INSTANT) ?: "null"

/*
 * The recycle-bin shape the desktop's own bin has to agree with. Staged with the same pinned library
 * rather than described, because the two questions it answers are about what Android can physically
 * put in a file: whether an entry in the bin carries the folder it came from, and what a deletion
 * record holds.
 */

private val probeOriginUuid = UUID.fromString("11111111-2222-3333-4444-555555555555")
private val probeBinUuid = UUID.fromString("66666666-7777-8888-9999-000000000001")
private val probeBinnedUuid = UUID.fromString("66666666-7777-8888-9999-000000000002")
private val probeLiveUuid = UUID.fromString("66666666-7777-8888-9999-000000000003")
private val probeDeletedUuid = UUID.fromString("66666666-7777-8888-9999-000000000004")

private fun probeBin(output: java.io.File) {
    val credentials = Credentials.from(EncryptedValue.fromString(FIXTURE_PASSWORD))
    val base = KeePassDatabase.Ver4x.create(
        rootName = "Root",
        meta = Meta(generator = "Monica Password Manager", name = "Bin Probe"),
        credentials = credentials
    )
    val salt = (base.header.kdfParameters as KdfParameters.Argon2).salt
    val shaped = base.copy(
        header = base.header.copy(
            cipherId = BaseCiphers.Aes.uuid,
            kdfParameters = KdfParameters.Argon2(
                variant = KdfParameters.Argon2.Variant.Argon2d,
                salt = salt,
                parallelism = 2U,
                memory = 32UL * 1024UL * 1024UL,
                iterations = 8U,
                version = 0x13U,
                secretKey = null,
                associatedData = null
            )
        )
    )
    val binned = Entry(
        uuid = probeBinnedUuid,
        times = fixedTimes(),
        fields = fields("probe-binned", "probe-user", "probe-ticket", listOf()),
        previousParentGroup = probeOriginUuid
    )
    val live = Entry(
        uuid = probeLiveUuid,
        times = fixedTimes(),
        fields = fields("probe-live", "probe-user", "probe-ticket", listOf())
    )
    val root = shaped.content.group.copy(
        entries = listOf(live),
        groups = listOf(
            Group(uuid = probeOriginUuid, name = "Probe Origin", times = fixedTimes()),
            Group(
                uuid = probeBinUuid,
                name = "Recycle Bin",
                times = fixedTimes(),
                entries = listOf(binned)
            )
        )
    )
    val meta = shaped.content.meta.copy(
        recycleBinEnabled = true,
        recycleBinUuid = probeBinUuid,
        recycleBinChanged = Instant.parse("2026-02-03T04:05:06Z")
    )
    val staged = shaped.copy(
        content = app.keemobile.kotpass.models.DatabaseContent(
            meta = meta,
            group = root,
            deletedObjects = listOf(
                app.keemobile.kotpass.models.DeletedObject(
                    probeDeletedUuid,
                    Instant.parse("2026-02-03T04:05:06Z")
                )
            )
        )
    )
    FileOutputStream(output).use {
        (staged as KeePassDatabase.Ver4x).encode(it, cipherProviders = cipherProviders)
    }
    println("probe-bin bytes=${output.length()}")
    verifyBin(output)
}

/// Read back through the same library, so the report is about the file rather than the model that was
/// handed to the encoder.
private fun verifyBin(file: java.io.File) {
    val credentials = Credentials.from(EncryptedValue.fromString(FIXTURE_PASSWORD))
    val database = FileInputStream(file).use {
        KeePassDatabase.decode(it, credentials, cipherProviders = cipherProviders)
    }
    val meta = database.content.meta
    println("format=KDBX-${database.header.version.major}.${database.header.version.minor} " +
        "generator=${meta.generator}")
    println("recycleEnabled=${meta.recycleBinEnabled} recycleUuid=${meta.recycleBinUuid} " +
        "recycleChanged=${iso(meta.recycleBinChanged)}")
    println("deleted=${database.content.deletedObjects.size} " +
        "deletedIds=${database.content.deletedObjects.map { it.id.toString() }} " +
        "deletedTimes=${database.content.deletedObjects.map { iso(it.deletionTime) }}")
    database.content.group.groups.forEach { group ->
        group.entries.forEach { entry ->
            println("entry group=${group.name} title=${entry.fields.title?.content} " +
                "origin=${entry.previousParentGroup} location=${iso(entry.times?.locationChanged)}")
        }
    }
}
