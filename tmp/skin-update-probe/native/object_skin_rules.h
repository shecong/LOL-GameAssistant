// Restored original1 object-selection rules. No process access in this layer.
#pragma once
#include <cstdint>
#include <cstring>
enum class ObjectSkinAction {Skip,LaneMinion,Ward,OwnerShape};
struct ObjectSkinObservation {
    bool laneMinion,hasOwner,ownedByLocal;
    int playerTeam,baseSkin,ownerSkin;
    const char* model;const char* playerModel;
};
struct ObjectSkinSelection {int minionIndex=-1,wardSkin=-1;};
struct ObjectSkinDecision {ObjectSkinAction action=ObjectSkinAction::Skip;int skin=-1;bool refresh=false,legacyBaseReset=false;};
static ObjectSkinDecision selectObjectSkin(const ObjectSkinObservation& object,const ObjectSkinSelection& selection) {
    ObjectSkinDecision result{};
    if(!object.model || !object.playerModel || object.baseSkin<0 || object.baseSkin>10000 ||
       selection.minionIndex < -1 || selection.minionIndex>4999 || selection.wardSkin < -1 || selection.wardSkin>10000)return result;
    if(object.laneMinion && selection.minionIndex>=0) {
        if(object.playerTeam!=1 && object.playerTeam!=2)return result;
        result.action=ObjectSkinAction::LaneMinion;
        result.skin=selection.minionIndex*2+(object.playerTeam==2?1:0);
    } else {
        bool ward=!strcmp(object.model,"JammerDevice") || !strcmp(object.model,"SightWard") ||
            !strcmp(object.model,"YellowTrinket") || !strcmp(object.model,"VisionWard") || !strcmp(object.model,"BlueTrinket");
        bool shape=!strcmp(object.model,"TestCubeRender10Vision");
        if(!object.hasOwner || !object.ownedByLocal || (!ward && !shape))return result;
        if(shape) {
            result.action=ObjectSkinAction::OwnerShape;
            result.skin=!strcmp(object.playerModel,"Yone")?object.ownerSkin:0;
        } else {
            if(selection.wardSkin<0)return result;
            result.action=ObjectSkinAction::Ward;result.skin=selection.wardSkin;
        }
    }
    if(result.skin<0 || result.skin>10000)return {};
    result.refresh=object.baseSkin!=result.skin;
    // Observed DLL RVA 0x56491. Expose the behavior for verification, do not
    // silently execute this reset on an unverified game object layout.
    result.legacyBaseReset=result.refresh && result.action!=ObjectSkinAction::OwnerShape;
    return result;
}
