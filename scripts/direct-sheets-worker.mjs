// Bundled with the Windows application. Credentials arrive through stdin, never arguments or logs.
import {runDirectSheets} from './lib/direct-sheets.ts';
import {ExportError} from './lib/sheets.ts';
try {
 let input='';for await(const chunk of process.stdin){input+=chunk;if(input.length>100000)throw new Error('Input too large');}
 const result=await runDirectSheets(JSON.parse(input));process.stdout.write(JSON.stringify(result));
} catch(error) {
 const code=error instanceof ExportError?error.code:'service';
 process.stdout.write(JSON.stringify({state:'retry',code}));
}
