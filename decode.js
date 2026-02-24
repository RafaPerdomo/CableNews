const str = 'CBMihgFBVV95cUxNTGFxYVlCVEo1NkUxVFVvXzBTU3RqWkFLYVFiTXVjcjRoMVhNLXpDUzhvQVBDSE5sQU0wTHlvWW5NRDVDM1NzQUpIYVl1dEZya01XTWN4S0tPQ1pDQWJxbWJFM0N2bHVhOXdjZktkaElXanhlR2dFOHRjeWZ5THI0dU1OdWJYdw';
const buf = Buffer.from(str, 'base64');
console.log('Decoded buffer string:', buf.toString('latin1'));
const match = buf.toString('latin1').match(/https?:\/\/[^\x00-\x1F\x7F"'\s]+/i);
if (match) {
    console.log('Found URL:', match[0]);
} else {
    console.log('No URL found!');
}
