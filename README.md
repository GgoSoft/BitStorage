# BitStorageir

I was working on another personal project (nothing big, just a "for fun" project) and I had to store a lot 
of bits. I thought it would be interesting to create a bit storage system that could store bits in a more
efficient and easier way than using a byte array or BitArray. My requirements were that I wanted to push an 
arbitrary number of bits and be able to read them all or just a few bits at a time.

As an example, I may want to store 3 bits, then 15 bits, then 87 bits, then 1 bit, then 2 bits, etc. and
be able to store all of them somewhere (file, memory, etc.). Later, I want to read all of them back in, then
be able to read 3 bits, then 15 bits, then 87 bits, then 1 bit, then 2 bits, (but the read bit lengths don't
have to agree with the written bit lengths, so 8 bits or whatever could have been read first instead of 3) etc. 
and have them all be in the same order that was written.

Because of these requirements, I decided that it would be easier to create a class that would handle the bit 
storage and retrieval instead of using BitArray.

I was debating on how to store the underlying bits.  I finally settled for a list of bytes.  I believe this
can easily be changed to a list of other numeric types, but I haven't tried.  The storage is also bit-endian,
meaning that if you push 0b1, it will be stored as 0b10000000, not 0b00000001.  Adding 0b011 will result in
0b10110000.  This is because I wanted to be able to read the bits back in the same order that they were written
and not have the underlying storage change.  I could have shifted the bits in, but that would mean that the
individual locations of the bits would change and I didn't want that.  The annoying thing about this is that
if you want to push 0b1, you have to push 0b10000000.  I did add a parameter to allow little-endian storage
and retrieval with the default as big endian, but this doesn't work for reading and writing arrays of values.

