// Run from the repo root after `cargo build -p showdevice` and packaging plugins.
// Uses only the bundled demo plugin; never queries physical devices.
const {spawn}=require('node:child_process');
const assert=require('node:assert/strict');
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
    const child=spawn('target/debug/showdevice.exe',['demo','dist/plugins'],{
        env:{...process.env,SHOWDEVICE_VISIBILITY_MANAGED:'1',SHOWDEVICE_POLL_PREFERENCES:JSON.stringify({interval_seconds:5})},windowsHide:true,
    });
    let buffer='',frames=[];
    child.stdout.on('data',data=>{
        buffer+=data;let index;
        while((index=buffer.indexOf('\n'))>=0){frames.push(JSON.parse(buffer.slice(0,index)));buffer=buffer.slice(index+1);}
    });
    child.stderr.on('data',data=>process.stderr.write(data));
    child.on('error',error=>{throw error;});
    const send=command=>child.stdin.write(JSON.stringify(command)+'\n');
    try{
        await wait(1700);
        assert(!frames.some(frame=>frame.devices.length),'hidden startup queried a device');
        send({type:'visibility',all:true,device_ids:[]});await wait(2600);
        assert(frames.some(frame=>frame.devices.length),'first reveal did not query');
        send({type:'visibility',all:false,device_ids:[]});await wait(800);
        const before=frames.length;await wait(5600);
        assert.equal(frames.length,before,'hidden device kept polling after its interval');
        send({type:'visibility',all:true,device_ids:[]});await wait(1600);
        assert(frames.length>before,'reveal did not update cached readings');
        send({type:'visibility',all:false,device_ids:['unrelated-device']});await wait(800);
        const scoped=frames.length;await wait(5600);
        assert.equal(frames.length,scoped,'unselected device kept polling');
        console.log('PASS core visibility: hidden startup, reveal, pause, re-reveal, selected devices');
    }finally{
        child.stdin.end();await wait(1000);
        if(child.exitCode===null)child.kill();
    }
})().catch(error=>{console.error(error);process.exitCode=1;});
