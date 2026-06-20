shader_type canvas_item;
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;

void fragment() {
    // Уменьшаем LOD для меньшего размытия фона
    vec4 color = textureLod(screen_texture, SCREEN_UV, 1.0);
    color.rgb *= 0.6; // Темнее для контраста
    color.a = 1.0;
    COLOR = color;
}
