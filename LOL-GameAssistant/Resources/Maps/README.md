# Summoner's Rift terrain reference

`summoners-rift-base.png` is the unmodified 512 × 512 base minimap texture from
League of Legends 16.21, distributed by CommunityDragon:

https://raw.communitydragon.org/16.21/game/assets/maps/info/map11/2dlevelminimap_base_baron1.png

The artwork belongs to Riot Games. CommunityDragon distributes game assets under
Riot's [Legal Jibber Jabber policy](https://www.riotgames.com/en/legal).

The resource is embedded for offline rendering. `JungleRouteMap` adds forest
shading, reference landmarks, labels and route overlays at runtime. The base map
shows the standard terrain; it is not a live display of elemental terrain,
building destruction or monster availability. Landmarks are reference locations.

Game positions use the existing 0–15000 coordinate domain; the drawing reverses
the vertical axis so blue side remains at the lower left. Zoom and pan apply to
both the terrain and every overlay through the same map rectangle.
