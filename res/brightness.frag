#version 330 core

in mediump vec2 v_texcoord;

out lowp vec4 f_color;

uniform lowp sampler2D u_texture;

void main() {
	lowp vec4 color = texture(u_texture, v_texcoord);
	//float brightness = (0.299*color.r + 0.587*color.g + 0.114*color.b);
	float brightness = max(max(color.r, color.g), color.b);
	f_color = vec4(brightness, brightness, brightness, 1);
}
