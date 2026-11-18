#!/bin/bash
# usage: [SDK=11.0.100] [WORK=dir] [ANALYZER=dir] Checked/run.sh [true|false]
#
# Generates CK.cs with gen_ck.py: every narrow type and its nullable, every compound operator and increment, in every form of
# checked context, each after a commit and between the read and the assignment of a [MovedFrom] parameter, with values that
# overflow. Builds it with the analyzer, with <CheckForOverflowUnderflow> set to the argument (false by default), runs every case,
# and prints the cases that the analyzer accepted and that released one acquisition twice (MISSES), with a summary line.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
SDK=${SDK:-11.0.100}
CHK=${1:-false}
WORK=${WORK:-${TMPDIR:-/tmp}/sca-stress}/checked-$SDK-$CHK
ANALYZER_DLL=${ANALYZER:+$ANALYZER/Singulink.CodeAnalysis.Analyzers.dll}
ANALYZER_DLL=${ANALYZER_DLL:-$REPO/Source/Singulink.CodeAnalysis.Analyzers/bin/Release/netstandard2.0/Singulink.CodeAnalysis.Analyzers.dll}
ATTRIBUTES_DLL=${ANALYZER:+$ANALYZER/Singulink.CodeAnalysis.dll}
ATTRIBUTES_DLL=${ATTRIBUTES_DLL:-$REPO/Source/Singulink.CodeAnalysis/bin/Release/net10.0/Singulink.CodeAnalysis.dll}

if [ ! -f "$ANALYZER_DLL" ]; then echo "no analyzer at $ANALYZER_DLL: build the repository in Release first"; exit 1; fi

win() { if command -v cygpath > /dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

rm -rf "$WORK"; mkdir -p "$WORK"
cp "$HERE/Common.cs" "$WORK/"
(cd "$WORK" && python "$HERE/gen_ck.py")
cat > "$WORK/p.csproj" <<EOP
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <CheckForOverflowUnderflow>$CHK</CheckForOverflowUnderflow>
    <NoWarn>\$(NoWarn);CS0168;CS0219;CS8321;CS0414;CS0649;CS0169;CS0162;CS0164;CS1717;CS1718;CS8600;CS8602;CS8604;CS8629;CS0183;CS8794;CS8793;CS1522;CS0642;CS8509;CS8846;CS0675;CS0652;CS8619;CS8618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Analyzer Include="$(win "$ANALYZER_DLL")" />
    <Reference Include="$(win "$ATTRIBUTES_DLL")" />
  </ItemGroup>
</Project>
EOP
printf '{ "sdk": { "version": "%s", "rollForward": "disable" } }\n' "$SDK" > "$WORK/global.json"
cd "$WORK"
dotnet build p.csproj -c Release --no-incremental -v:q -clp:NoSummary > build.log 2>&1 || true
grep -oE "[A-Za-z0-9_]+\.cs\([0-9]+,[0-9]+\): (warning|error) [A-Z]+[0-9]+: .{0,150}" build.log | sort -u > diags.txt || true
echo "AD0001: $(grep -c AD0001 build.log || true)"

if grep -q " error " build.log; then
  grep " error " build.log | head -5
  exit 1
fi

dotnet bin/Release/net10.0/p.dll > run.txt 2>&1 || true
python "$HERE/verdict.py" "$WORK"
