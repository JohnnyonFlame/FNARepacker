DEPS := \
	deps/FNA.dll \
	deps/DotNetZip.dll

SRC := \
	TextureData.cs \
	BinaryExts.cs \
	Program.cs \
	AssetLoader.cs

COMFIGURATION?=Release
MONO_PREFIX?=
ifneq ($(MONO_PREFIX),)
MONO := $(MONO_PREFIX)/bin/mono
MONO_PATH := $(MONO_PREFIX)/lib/mono/4.5
else
MONO := mono
MONO_DIR := $(shell dirname $(realpath $(shell which mono)))
MONO_PATH := $(MONO_DIR)/lib/mono/4.5
endif

COMMON_DEPS := \
	$(MONO_PATH)/Facades/netstandard.dll \
	$(MONO_PATH)/mscorlib.dll \
	$(MONO_PATH)/Facades/System.Runtime.dll \
	$(wildcard $(MONO_PATH)/*.dll)

VISFREE ?= VisFree.exe

.PHONY: .all
all: $(SRC)
	@mkdir -p bin/
	@MONO_PATH=$(MONO_PATH) $(MONO) $(VISFREE) bin/FNARepacker.exe $(SRC) -- $(COMMON_DEPS) $(DEPS)
