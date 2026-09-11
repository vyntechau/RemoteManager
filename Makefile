# ==============================================================================
# Vyntech Remote Manager Makefile
# ==============================================================================
# Provides standard build, test, and release packaging targets.
# Wraps PowerShell build automation with cross-environment make commands.
# ==============================================================================

SHELL := powershell.exe
.SHELLFLAGS := -NoProfile -ExecutionPolicy Bypass -Command

CONFIGURATION ?= Release
VERSION ?= 1.0.0
OUTPUT_DIR ?= artifacts

.PHONY: help all build test exe msi package clean restore

help:
	@Write-Host "Vyntech Remote Manager Build Targets:" -ForegroundColor Cyan
	@Write-Host "  make all         - Run tests, build Portable EXE ZIP, MSI installer & checksums" -ForegroundColor White
	@Write-Host "  make build       - Compile solution ($(CONFIGURATION))" -ForegroundColor White
	@Write-Host "  make test        - Run all unit tests" -ForegroundColor White
	@Write-Host "  make exe         - Publish self-contained single-file EXE and create portable ZIP" -ForegroundColor White
	@Write-Host "  make msi         - Build Windows Installer (.msi) using WiX v5" -ForegroundColor White
	@Write-Host "  make package     - Package both Portable ZIP and MSI installer with checksums" -ForegroundColor White
	@Write-Host "  make restore     - Restore NuGet dependencies and local dotnet tools" -ForegroundColor White
	@Write-Host "  make clean       - Remove all build artifacts, bin, obj, and temp files" -ForegroundColor White
	@Write-Host ""
	@Write-Host "Variables:" -ForegroundColor Cyan
	@Write-Host "  CONFIGURATION=$(CONFIGURATION)" -ForegroundColor Gray
	@Write-Host "  VERSION=$(VERSION)" -ForegroundColor Gray
	@Write-Host "  OUTPUT_DIR=$(OUTPUT_DIR)" -ForegroundColor Gray

restore:
	dotnet tool restore
	dotnet restore RemoteManager.sln

build:
	dotnet build RemoteManager.sln -c $(CONFIGURATION) --nologo

test:
	dotnet test tests/RemoteManager.Tests/RemoteManager.Tests.csproj -c $(CONFIGURATION) --nologo --verbosity normal

exe:
	powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Target Exe -Configuration $(CONFIGURATION) -Version $(VERSION) -OutputDir $(OUTPUT_DIR)

msi:
	powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Target Msi -Configuration $(CONFIGURATION) -Version $(VERSION) -OutputDir $(OUTPUT_DIR)

package:
	powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Target Package -Configuration $(CONFIGURATION) -Version $(VERSION) -OutputDir $(OUTPUT_DIR)

all:
	powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Target All -Configuration $(CONFIGURATION) -Version $(VERSION) -OutputDir $(OUTPUT_DIR)

clean:
	powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Target Clean -OutputDir $(OUTPUT_DIR)
