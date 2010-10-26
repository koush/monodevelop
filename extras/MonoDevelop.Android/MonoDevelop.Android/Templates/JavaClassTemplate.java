package {0};

import com.koushikdutta.monojavabridge.MonoBridge;
import com.koushikdutta.monojavabridge.MonoProxy;

public class {1} extends {2} implements MonoProxy{5}
{{
	static
	{{
{3}
	}}

{4}

	long myGcHandle;
	public long getGCHandle() {{
		return myGcHandle;
	}}

	public void setGCHandle(long gcHandle) {{
		myGcHandle = gcHandle;
	}}
}}
