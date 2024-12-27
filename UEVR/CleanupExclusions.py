from os import scandir, rename, remove, chdir, makedirs, getcwd,environ
from os.path import isfile, isdir, join

#  doesn't need to ship with app only wrote this to give to people not using the fork


knownfiles = [
    "bindings_vive_controller.json",
    "cameras.txt",
    "actions.json",
    "binding_rift.json",
    "binding_vive.json",
    "bindings_knuckles.json",
    "bindings_oculus_touch.json"]

def file_count(path):
    if not isdir(path):
        return 0
    count = 0
    for f in scandir(path):
        if isfile(f.path):
            count += 1
    return count

def validate_log(path):
    with open(path, "r") as txt:
        for line in txt.readlines():
            if line.endswith("Framework initialized"):
                return True
        txt.seek(0)
        txt.close()
    return False


unrealvrmod = join(environ['APPDATA'], "UnrealVRMod")

if not getcwd() == unrealvrmod:
    chdir(unrealvrmod)

uevr = join(environ['APPDATA'], "UnrealVRMod", "UEVR")
uevr_nightly  = join(environ['APPDATA'], "UnrealVRMod", "uevr-nightly")
profiles = [ join(unrealvrmod, x.name) for x in scandir(unrealvrmod) if isdir(x)]
removals = []
for prof in profiles:
    if prof.lower().endswith("win64-shipping"): continue
    if "uevr" in prof.lower(): continue
    if prof == uevr: continue
    if prof == uevr_nightly: continue
    if (file_count(plugins := join(prof, "plugins")) >= 1): continue
    if (file_count(scripts := join(prof, "scripts")) >= 1): continue
    if (file_count(uobjecthook := join(prof,"uobjecthook")) >= 1): continue
    if (file_count(sdkdump := join(prof, "sdkdump")) >= 1): continue
    if any([isfile(join(prof,f)) for f in knownfiles]): continue
    if isfile(log:=join(prof,"log.txt")) and validate_log(log): continue
    removals.append(prof.rsplit("\\")[-1])

makedirs(exclude:=join(unrealvrmod, "excluded"), exist_ok = True)
for removal in removals:
    print(removal)
    rename(join(unrealvrmod, removal), join(exclude, removal))