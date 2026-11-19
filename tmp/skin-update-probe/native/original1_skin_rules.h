#pragma once
// Original1 DLL cf4ed878813631efaf4018ad7cfa8354ab10b3b395a29ca64411e13590a6d3b2.
// Recovered branch function RVA 0x562e0..0x5667c. Lux/Sona stack handling
// remains in the caller; this function returns only the base layer gear value.
static int original1DefaultGear(const char* hero,int skin,int current) {
    if(!strcmp(hero,"Kayn") || !strcmp(hero,"Lux") || !strcmp(hero,"Sona"))return current;
    if((!strcmp(hero,"Morgana") || !strcmp(hero,"Tristana")) && skin==80)return 3;
    if((!strcmp(hero,"Katarina") && skin>=29 && skin<=36) || (!strcmp(hero,"Jinx") && skin==60) ||
       (!strcmp(hero,"Ezreal") && skin==5) || (!strcmp(hero,"Mordekaiser") && skin==54) ||
       (!strcmp(hero,"Leona") && skin==64) || (!strcmp(hero,"Caitlyn") && skin==22) ||
       (!strcmp(hero,"Jax") && skin>=14 && skin<=19) || (!strcmp(hero,"Sett") && skin==66) ||
       (!strcmp(hero,"Zeri") && skin==40) || (!strcmp(hero,"Kayle") && skin==6) ||
       (!strcmp(hero,"Kaisa") && skin==71) || (!strcmp(hero,"Renekton") && skin>=26 && skin<=32) ||
       (!strcmp(hero,"Thresh") && skin==19) || (!strcmp(hero,"Lillia") && skin==46) ||
       (!strcmp(hero,"Aatrox") && skin==33) || (!strcmp(hero,"Evelynn") && skin==24) ||
       (!strcmp(hero,"Ahri") && skin==86) || (!strcmp(hero,"Viego") && skin==43))return 0;
    return -1;
}
