#!/bin/bash
# usage: [SDK=11.0.100] [WORK=dir] [ANALYZER=dir] Flow/run.sh LABEL GENERATOR MODE COUNT SEED...
#
# Generates one file of COUNT methods per SEED with GENERATOR (gen_lz.py, gen_nest.py or gen_nest2.py) in MODE, builds them with
# the analyzer, runs every method under 128 patterns of its conditions and a throw at each of ten positions, and prints every
# method that the analyzer accepted and that released one acquisition twice (a MISS), with a summary line.
#
# ANALYZER is the folder with Singulink.CodeAnalysis.Analyzers.dll and Singulink.CodeAnalysis.dll; by default the Release build of
# this repository. WORK is where the generated project goes, outside the repository by default.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
SDK=${SDK:-11.0.100}
LABEL=$1; GEN=$2; MODE=$3; COUNT=$4; shift 4
WORK=${WORK:-${TMPDIR:-/tmp}/sca-stress}/flow-$LABEL-$SDK
ANALYZER_DLL=${ANALYZER:+$ANALYZER/Singulink.CodeAnalysis.Analyzers.dll}
ANALYZER_DLL=${ANALYZER_DLL:-$REPO/Source/Singulink.CodeAnalysis.Analyzers/bin/Release/netstandard2.0/Singulink.CodeAnalysis.Analyzers.dll}
ATTRIBUTES_DLL=${ANALYZER:+$ANALYZER/Singulink.CodeAnalysis.dll}
ATTRIBUTES_DLL=${ATTRIBUTES_DLL:-$REPO/Source/Singulink.CodeAnalysis/bin/Release/net10.0/Singulink.CodeAnalysis.dll}

if [ ! -f "$ANALYZER_DLL" ]; then echo "no analyzer at $ANALYZER_DLL: build the repository in Release first"; exit 1; fi

win() { if command -v cygpath > /dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

rm -rf "$WORK"; mkdir -p "$WORK"
cp "$HERE/Common.cs" "$HERE/Program.cs" "$WORK/"
cat > "$WORK/lz.csproj" <<EOP
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <NoWarn>\$(NoWarn);CS0168;CS0219;CS8321;CS0414;CS0649;CS0169;CS0162;CS0164;CS1717;CS1718;CS8600;CS8602;CS8604;CS8629;CS0183;CS8794;CS8793;CS1522;CS0642;CS8509;CS8846</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Analyzer Include="$(win "$ANALYZER_DLL")" />
    <Reference Include="$(win "$ATTRIBUTES_DLL")" />
  </ItemGroup>
</Project>
EOP
printf '{ "sdk": { "version": "%s", "rollForward": "disable" } }\n' "$SDK" > "$WORK/global.json"
cd "$WORK"
printf 'public static class Tables\n{\n    public static void RunAll()\n    {\n' > Tables.cs

for seed in "$@"; do
  NAME=$(python "$HERE/$GEN" $seed $COUNT $MODE | cut -d' ' -f1)
  echo "        ${NAME}Table.RunAll();" >> Tables.cs
done

printf '    }\n}\n' >> Tables.cs
dotnet build lz.csproj -c Release --no-incremental -v:q -clp:NoSummary > build.log 2>&1 || true
echo "AD0001: $(grep -c AD0001 build.log || true)"
grep -E " error " build.log | sed -E 's/.*\.cs\(([0-9]+),[0-9]+\): //; s/\[.*//' | sort | uniq -c | sort -rn | head -12 || true
rm -f run.txt skip.txt current.txt

# A method that crashes the process is written to current.txt by the harness, skipped, and the run goes on.
if ! grep -q " error " build.log; then
  for attempt in $(seq 1 40); do
    DOTNET_TieredCompilation=${TC:-0} DOTNET_TieredPGO=0 timeout 300 dotnet bin/Release/net10.0/lz.dll >> run.txt 2>/dev/null || true
    if [ ! -f current.txt ]; then break; fi
    echo "RUNTIME FAILURE in $(cat current.txt)"; cat current.txt >> skip.txt; echo >> skip.txt; rm -f current.txt
  done
fi

python "$HERE/verdict.py" build.log run.txt "$WORK" [A-Z][0-9]*.cs
