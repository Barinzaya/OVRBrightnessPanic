#version 330 core

in highp   vec2 a_pos;
in mediump vec2 a_texcoord;

out mediump vec2 v_texcoord;

void main()
{
	gl_Position = vec4(a_pos, 0, 1);
	v_texcoord = a_texcoord;
}
