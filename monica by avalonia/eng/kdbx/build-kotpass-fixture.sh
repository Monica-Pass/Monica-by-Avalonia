#!/usr/bin/env bash
# kotpass <-> desktop KDBX shape harness. Builds a small Kotlin program against the real
# kotpass 0.10.0 jar Android uses, so the fixture is authored by the same library, not by us.
set -euo pipefail

CACHE="C:/Users/joyins/Desktop/Monica-all/Monica-main/Monica for Android/.gradle-user-home/caches/modules-2/files-2.1"
KOTPASS="$CACHE/app.keemobile/kotpass/0.10.0/b0a5fa5aba7bdd148bae7dcb351c7b229b624f5e/kotpass-0.10.0.jar"
XMLB="$CACHE/org.redundent/kotlin-xml-builder/1.9.1/b5526e0b8149fb3393d110f21ed1321f972fb018/kotlin-xml-builder-1.9.1.jar"
OKIO="$CACHE/com.squareup.okio/okio-jvm/3.9.0/eaa4f7858a1e80908b1a2e861e662edd7c6cbbb5/okio-jvm-3.9.0.jar"
SLF4J="$CACHE/org.slf4j/slf4j-api/2.0.16/172931663a09a1fa515567af5fbef00897d3c04/slf4j-api-2.0.16.jar"
STDLIB="$CACHE/org.jetbrains.kotlin/kotlin-stdlib/2.0.21/618b539767b4899b4660a83006e052b63f1db551/kotlin-stdlib-2.0.21.jar"
REFLECT="$CACHE/org.jetbrains.kotlin/kotlin-reflect/2.0.21/669e1d35e4ca1797f9ddb2830dd6c36c0ca531e4/kotlin-reflect-2.0.21.jar"
SCRIPTRT="$CACHE/org.jetbrains.kotlin/kotlin-script-runtime/2.0.21/c9b044380ad41f89aa89aa896c2d32a8c0b2129d/kotlin-script-runtime-2.0.21.jar"
DAEMON="$CACHE/org.jetbrains.kotlin/kotlin-daemon-embeddable/2.0.21/c9e933b23287de9b5a17e2116b4657bb91aea72c/kotlin-daemon-embeddable-2.0.21.jar"
COROUTINES="$CACHE/org.jetbrains.kotlinx/kotlinx-coroutines-core-jvm/1.10.2/4a9f78ef49483748e2c129f3d124b8fa249dafbf/kotlinx-coroutines-core-jvm-1.10.2.jar"
ANNOTATIONS_UNUSED=""
TROVE="$CACHE/org.jetbrains.intellij.deps/trove4j/1.0.20200330/3afb14d5f9ceb459d724e907a21145e8ff394f02/trove4j-1.0.20200330.jar"
COMPILER="$CACHE/org.jetbrains.kotlin/kotlin-compiler-embeddable/2.0.21/79346ed53db48b18312a472602eb5c057070c54d/kotlin-compiler-embeddable-2.0.21.jar"

ROOT="$(cygpath -m "$(cd "$(dirname "$0")" && pwd)")"
LANG3="$CACHE/org.apache.commons/commons-lang3/3.5/6c6c702c89bfff3cd9e80b04d668c5e190d588c6/commons-lang3-3.5.jar"
CP="$KOTPASS;$XMLB;$OKIO;$SLF4J;$STDLIB;$LANG3"
ANNOTATIONS="$CACHE/org.jetbrains/annotations/13.0/919f0dfe192fb4e063e7dacadee7f8bb9a2672a9/annotations-13.0.jar"
COMPILE_CP="$COMPILER;$STDLIB;$REFLECT;$SCRIPTRT;$DAEMON;$COROUTINES;$TROVE;$ANNOTATIONS"
OUT="$ROOT/out"
SRC="$ROOT/harness/src/Main.kt"

ARGS=("$@")
if [[ "${1:-}" == "build" ]]; then
  rm -rf "$OUT"
  ARGS=("${@:2}")
fi

if [[ ! -f "$OUT/parity/MainKt.class" ]]; then
  java -cp "$COMPILE_CP" org.jetbrains.kotlin.cli.jvm.K2JVMCompiler \
    -nowarn -no-stdlib -no-reflect -classpath "$CP" -d "$OUT" "$SRC" >&2
  if [[ ${#ARGS[@]} -eq 0 ]]; then
    exit 0
  fi
fi

java -cp "$OUT;$CP" parity.MainKt "${ARGS[@]}"
